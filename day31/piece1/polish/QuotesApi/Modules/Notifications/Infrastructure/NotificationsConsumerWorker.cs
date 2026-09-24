using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Modules.Notifications.Application;
using QuotesApi.Shared.Infrastructure.Telemetry;

namespace QuotesApi.Modules.Notifications.Infrastructure;

/// <summary>
/// Service Bus -> notifications. The consuming half of the pipeline whose producing half is
/// <see cref="OutboxRelayWorker"/> (database -> Service Bus); the two never share a code path.
///
/// Uses a ServiceBusProcessor on the dedicated notifications subscription with
/// AutoCompleteMessages = false, so a message is only ever completed after
/// QuoteCreatedNotificationHandler has finished and the notification is durably saved (or was
/// already saved by an earlier delivery). Anything else is either dead-lettered (bad data) or
/// abandoned (transient failure) so Service Bus redelivers it.
///
/// Delivery is at-least-once end to end: the relay can re-publish after a crash, and a
/// complete can be lost after a successful save. Both show up here as a repeated MessageId,
/// which the handler turns into a no-op via the unique SourceMessageId index.
///
/// The processor is a singleton, but QuotesDbContext is scoped — so every message gets its own
/// DI scope via IServiceScopeFactory, just like the relay's per-poll scope.
/// </summary>
public sealed class NotificationsConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusOptions _serviceBusOptions;
    private readonly NotificationConsumerOptions _consumerOptions;
    private readonly ILogger<NotificationsConsumerWorker> _logger;
    private ServiceBusProcessor? _processor;

    public NotificationsConsumerWorker(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<NotificationConsumerOptions> consumerOptions,
        ILogger<NotificationsConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _serviceBusClient = serviceBusClient;
        _serviceBusOptions = serviceBusOptions.Value;
        _consumerOptions = consumerOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_serviceBusOptions.Topic))
        {
            throw new InvalidOperationException("ServiceBus:Topic is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_consumerOptions.SubscriptionName))
        {
            throw new InvalidOperationException(
                "ServiceBus:Notifications:SubscriptionName is not configured.");
        }

        _processor = _serviceBusClient.CreateProcessor(
            _serviceBusOptions.Topic,
            _consumerOptions.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, _consumerOptions.MaxConcurrentCalls),
                PrefetchCount = 0
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        _logger.LogInformation(
            "Notifications consumer starting. Topic={Topic} Subscription={Subscription} MaxConcurrentCalls={MaxConcurrentCalls}",
            _serviceBusOptions.Topic,
            _consumerOptions.SubscriptionName,
            _processor.MaxConcurrentCalls);

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown; StopAsync stops the processor
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        var messageType = message.ApplicationProperties.TryGetValue("MessageType", out var value)
            ? value?.ToString()
            : null;

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = message.MessageId,
            ["MessageType"] = messageType
        });
        using var activity = QuotesTelemetry.ActivitySource.StartActivity(
            "notifications.process-message",
            ActivityKind.Consumer);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.message.type", messageType);
        activity?.SetTag("messaging.delivery_count", message.DeliveryCount);

        _logger.LogInformation(
            "Received message {MessageId} MessageType={MessageType} DeliveryCount={DeliveryCount} from {Subscription}.",
            message.MessageId,
            messageType,
            message.DeliveryCount,
            _consumerOptions.SubscriptionName);

        if (!string.Equals(
                messageType,
                QuoteCreatedNotificationHandler.MessageType,
                StringComparison.Ordinal))
        {
            // Not an event this consumer creates notifications for. Retrying can't change that,
            // so it is completed (acknowledged) rather than left to cycle into the DLQ.
            _logger.LogInformation(
                "Ignoring message {MessageId}: MessageType={MessageType} is not handled by the notifications consumer.",
                message.MessageId,
                messageType);
            await args.CompleteMessageAsync(message, args.CancellationToken);
            return;
        }

        NotificationHandlingOutcome outcome;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<QuoteCreatedNotificationHandler>();
            outcome = await handler.HandleAsync(
                message.MessageId,
                message.Body.ToString(),
                args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Transient or unknown failure (database unavailable, simulated failure, shutdown
            // mid-message): nothing was saved, so the message must NOT be completed. Abandon
            // releases the lock now so Service Bus redelivers with DeliveryCount + 1; after the
            // subscription's MaxDeliveryCount it dead-letters it by itself.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(
                ex,
                "Processing failed for message {MessageId} (DeliveryCount={DeliveryCount}); abandoning for redelivery.",
                message.MessageId,
                message.DeliveryCount);
            await AbandonAsync(args);
            return;
        }

        switch (outcome.Result)
        {
            case NotificationHandlingResult.Created:
                _logger.LogInformation(
                    "Notification {NotificationId} created for message {MessageId}.",
                    outcome.NotificationId,
                    message.MessageId);
                await args.CompleteMessageAsync(message, args.CancellationToken);
                break;

            case NotificationHandlingResult.Duplicate:
                _logger.LogInformation(
                    "Duplicate message {MessageId} detected: notification already exists; completing without changes.",
                    message.MessageId);
                await args.CompleteMessageAsync(message, args.CancellationToken);
                break;

            case NotificationHandlingResult.Invalid:
                activity?.SetStatus(ActivityStatusCode.Error, outcome.Reason);
                _logger.LogWarning(
                    "Dead-lettering message {MessageId}: {Reason}",
                    message.MessageId,
                    outcome.Reason);
                await args.DeadLetterMessageAsync(
                    message,
                    deadLetterReason: "InvalidQuoteCreatedEvent",
                    deadLetterErrorDescription: outcome.Reason,
                    cancellationToken: args.CancellationToken);
                break;
        }
    }

    private async Task AbandonAsync(ProcessMessageEventArgs args)
    {
        try
        {
            // CancellationToken.None: during shutdown args.CancellationToken is already
            // cancelled, and the abandon should still be attempted.
            await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Lock already lost, or the connection is gone. The message is still not
            // completed, so Service Bus redelivers it once the lock expires.
            _logger.LogWarning(
                ex,
                "Abandon failed for message {MessageId}; it will be redelivered after its lock expires.",
                args.Message.MessageId);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus processor error. ErrorSource={ErrorSource} Entity={EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
