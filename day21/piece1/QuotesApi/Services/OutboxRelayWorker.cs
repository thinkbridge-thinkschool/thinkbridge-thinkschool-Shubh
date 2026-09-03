using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

/// <summary>
/// Transactional outbox relay. Polls <c>OutboxMessages</c> for rows with a null
/// <c>ProcessedOnUtc</c>, publishes each one's payload to the Service Bus topic, and only
/// once Service Bus has confirmed the send does it stamp <c>ProcessedOnUtc</c> and save.
///
/// This gives at-least-once delivery, not exactly-once: if the process dies after the
/// publish succeeds but before the database save commits, the row is still unprocessed on
/// restart and gets published again with the SAME MessageId (the outbox row's Id). A
/// message can therefore be delivered more than once, but it can never be silently lost —
/// consumers are expected to dedupe on MessageId (see Day 19's ServiceBusConsumer).
///
/// A scoped QuotesDbContext cannot be injected directly into this singleton
/// BackgroundService, so a new scope is created per polling cycle via
/// IServiceScopeFactory instead.
/// </summary>
public sealed class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusOptions _serviceBusOptions;
    private readonly OutboxRelayOptions _relayOptions;
    private readonly OutboxCrashSimulator _crashSimulator;
    private readonly ILogger<OutboxRelayWorker> _logger;
    private ServiceBusSender? _sender;

    public OutboxRelayWorker(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<OutboxRelayOptions> relayOptions,
        OutboxCrashSimulator crashSimulator,
        ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _serviceBusClient = serviceBusClient;
        _serviceBusOptions = serviceBusOptions.Value;
        _relayOptions = relayOptions.Value;
        _crashSimulator = crashSimulator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox relay worker started. Topic={Topic} PollingIntervalSeconds={PollingIntervalSeconds} BatchSize={BatchSize}",
            _serviceBusOptions.Topic,
            _relayOptions.PollingIntervalSeconds,
            _relayOptions.BatchSize);

        _sender = _serviceBusClient.CreateSender(_serviceBusOptions.Topic);

        var pollingDelay = TimeSpan.FromSeconds(
            Math.Max(1, _relayOptions.PollingIntervalSeconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RelayPendingMessagesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A failure in the batch-fetch itself (e.g. a transient DB error) must not
                    // take the whole worker down — log and try again on the next tick.
                    _logger.LogError(
                        ex,
                        "Outbox relay batch failed unexpectedly; will retry on the next polling cycle.");
                }

                await Task.Delay(pollingDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }

        _logger.LogInformation("Outbox relay worker stopped.");
    }

    private async Task RelayPendingMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_relayOptions.BatchSize)
            .ToListAsync(stoppingToken);

        if (pending.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Outbox relay found {PendingCount} unprocessed message(s).",
            pending.Count);

        foreach (var outboxMessage in pending)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await RelaySingleMessageAsync(db, outboxMessage, stoppingToken);
        }
    }

    private async Task RelaySingleMessageAsync(
        QuotesDbContext db,
        Models.Outbox.OutboxMessage outboxMessage,
        CancellationToken stoppingToken)
    {
        try
        {
            var serviceBusMessage = new ServiceBusMessage(outboxMessage.Payload)
            {
                MessageId = outboxMessage.Id.ToString(),
                ContentType = "application/json"
            };
            serviceBusMessage.ApplicationProperties["MessageType"] =
                outboxMessage.MessageType;

            // ONLY once this returns without throwing has Service Bus durably accepted the
            // message. Everything below this line runs "after the crash window" in the
            // normal path; OutboxCrashSimulator lets us prove what happens if the process
            // dies before reaching the SaveChangesAsync below.
            await _sender!.SendMessageAsync(serviceBusMessage, stoppingToken);

            _logger.LogInformation(
                "Published OutboxMessage {OutboxMessageId} (MessageType={MessageType}) to topic {Topic}.",
                outboxMessage.Id,
                outboxMessage.MessageType,
                _serviceBusOptions.Topic);

            _crashSimulator.MaybeCrashAfterPublish(outboxMessage.Id);

            outboxMessage.ProcessedOnUtc = DateTime.UtcNow;
            outboxMessage.Error = null;
            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "OutboxMessage {OutboxMessageId} marked processed at {ProcessedOnUtc}.",
                outboxMessage.Id,
                outboxMessage.ProcessedOnUtc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Publish failed (or the DB save after it failed): leave ProcessedOnUtc NULL so
            // the next polling cycle retries this same row. Record the error for visibility
            // but never let one bad message stop the rest of the batch or the worker.
            outboxMessage.Error = ex.Message;

            _logger.LogError(
                ex,
                "Failed to relay OutboxMessage {OutboxMessageId}; it remains unprocessed and will be retried.",
                outboxMessage.Id);

            try
            {
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(
                    saveEx,
                    "Failed to persist the error state for OutboxMessage {OutboxMessageId}.",
                    outboxMessage.Id);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
