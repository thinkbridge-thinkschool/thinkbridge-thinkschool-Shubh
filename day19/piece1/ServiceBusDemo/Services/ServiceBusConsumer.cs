using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Models;

namespace ServiceBusDemo.Services;

/// <summary>
/// Wraps a single ServiceBusProcessor bound to one subscription.
///
/// Competing consumers: create multiple ServiceBusConsumer instances that all point at the SAME
/// topic + subscription (e.g. "sub-a") with different <paramref name="workerName"/> labels. Azure
/// Service Bus's PeekLock delivery hands each message to exactly one of the connected
/// receivers/processors — the messages are load-balanced ("competing") across whichever worker
/// asks for the next message first, never delivered to more than one of them at once. A separate
/// ServiceBusConsumer pointed at a different subscription (e.g. "sub-b") is an independent
/// consumer group: because it's a different subscription entity, Service Bus copies every
/// matching topic message into it too, so subscription B sees its own full stream regardless of
/// what subscription A's workers already consumed.
/// </summary>
public class ServiceBusConsumer : IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly string _workerName;
    private readonly string _subscriptionName;
    private readonly MessageDeduplicationStore _dedupStore;

    public ServiceBusConsumer(
        ServiceBusClient client,
        string topicName,
        string subscriptionName,
        string workerName,
        MessageDeduplicationStore dedupStore)
    {
        _workerName = workerName;
        _subscriptionName = subscriptionName;
        _dedupStore = dedupStore;

        // AutoCompleteMessages = false: we decide explicitly whether to Complete or Abandon,
        // which is required both for the idempotency skip-path and for the poison-message retry path.
        _processor = client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
            PrefetchCount = 0
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var eventType = args.Message.ApplicationProperties.TryGetValue("EventType", out var et)
            ? et?.ToString()
            : "Unknown";

        // --- Idempotency check (MessageId as the dedup key) ---
        // Reserve BEFORE processing (atomic, thread-safe) so two competing consumers that happen
        // to receive two genuinely duplicate copies of the same MessageId at the same time can't
        // both process it. If processing then fails below, the reservation is released so a real
        // Service Bus redelivery (after Abandon) gets to retry instead of being wrongly treated as
        // an already-handled duplicate.
        if (!_dedupStore.TryReserve(_subscriptionName, messageId))
        {
            Console.WriteLine($"[{_subscriptionName}/{_workerName}] Duplicate MessageId={messageId} skipped");
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        Console.WriteLine($"[{_subscriptionName}/{_workerName}] Processing MessageId={messageId} EventType={eventType} DeliveryCount={args.Message.DeliveryCount}");

        try
        {
            var body = args.Message.Body.ToString();

            // Poison messages have deliberately malformed JSON -> this throws every time.
            var quote = JsonSerializer.Deserialize<QuoteEvent>(body)
                ?? throw new InvalidOperationException("Message body deserialized to null.");

            if (quote.QuoteId <= 0 || string.IsNullOrWhiteSpace(quote.Author))
            {
                throw new InvalidOperationException(
                    $"Invalid QuoteEvent payload: QuoteId and Author are required (QuoteId={quote.QuoteId}, Author='{quote.Author}').");
            }

            // Simulate real work; also widens the timing window so competing consumers visibly interleave.
            await Task.Delay(300, CancellationToken.None);

            Console.WriteLine($"[{_subscriptionName}/{_workerName}] OK MessageId={messageId} Quote #{quote.QuoteId} by {quote.Author}: \"{quote.Text}\"");
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_subscriptionName}/{_workerName}] FAILED MessageId={messageId} (DeliveryCount={args.Message.DeliveryCount}): {ex.Message}");

            // Release the dedup reservation: this attempt did NOT succeed, so the next delivery
            // (or dead-letter) of this MessageId must not be mistaken for an already-handled duplicate.
            _dedupStore.Release(_subscriptionName, messageId);

            // Abandon releases the lock immediately and increments DeliveryCount, letting Service
            // Bus redeliver right away instead of waiting for the lock to expire. Once DeliveryCount
            // reaches the subscription's MaxDeliveryCount, Service Bus itself moves the message to
            // the subscription's Dead-Letter Queue with reason "MaxDeliveryCountExceeded" — no
            // application code ever moves it there manually.
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        Console.WriteLine($"[{_subscriptionName}/{_workerName}] Processor error (source={args.ErrorSource}): {args.Exception.Message}");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _processor.StartProcessingAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _processor.StopProcessingAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
    }
}
