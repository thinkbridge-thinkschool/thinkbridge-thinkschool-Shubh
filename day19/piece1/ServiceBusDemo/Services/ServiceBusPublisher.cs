using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Models;

namespace ServiceBusDemo.Services;

/// <summary>
/// Publishes QuoteEvent messages to the Service Bus topic. Every message gets an explicit,
/// application-assigned MessageId (used downstream as the idempotency key) and an "EventType"
/// application property so consumers can branch on message shape without deserializing first.
/// </summary>
public class ServiceBusPublisher : IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public ServiceBusPublisher(ServiceBusClient client, string topicName)
    {
        _sender = client.CreateSender(topicName);
    }

    /// <summary>Publishes a well-formed QuoteEvent.</summary>
    public async Task PublishQuoteEventAsync(QuoteEvent quoteEvent, string messageId, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(quoteEvent);
        var message = BuildMessage(json, messageId, eventType: "QuoteCreated");
        message.ApplicationProperties["Author"] = quoteEvent.Author;

        await _sender.SendMessageAsync(message, cancellationToken);
        Console.WriteLine($"[Publisher] Sent MessageId={messageId} QuoteId={quoteEvent.QuoteId} Author={quoteEvent.Author}");
    }

    /// <summary>
    /// Publishes a deliberately invalid ("poison") message body. Consumers will fail to
    /// deserialize/validate it every time, so Service Bus will exhaust delivery attempts and
    /// dead-letter it for real — nothing about the DLQ outcome is faked here.
    /// </summary>
    public async Task PublishPoisonMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        const string invalidJson = "{ this is not valid JSON and will fail to deserialize ";
        var message = BuildMessage(invalidJson, messageId, eventType: "PoisonQuoteEvent");

        await _sender.SendMessageAsync(message, cancellationToken);
        Console.WriteLine($"[Publisher] Sent POISON MessageId={messageId}");
    }

    private static ServiceBusMessage BuildMessage(string body, string messageId, string eventType)
    {
        var message = new ServiceBusMessage(body)
        {
            MessageId = messageId,
            ContentType = "application/json",
            Subject = eventType
        };
        message.ApplicationProperties["EventType"] = eventType;
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
