using Azure.Messaging.ServiceBus;

namespace ServiceBusDemo.Services;

/// <summary>
/// Reads real dead-lettered messages from a subscription's DLQ sub-queue and prints the evidence
/// Service Bus itself recorded (MessageId, DeadLetterReason, DeadLetterErrorDescription). This
/// never fabricates a DLQ entry — it only reports what is actually sitting in the DLQ.
/// </summary>
public class DeadLetterInspector
{
    private readonly ServiceBusClient _client;

    public DeadLetterInspector(ServiceBusClient client)
    {
        _client = client;
    }

    /// <returns>Number of dead-lettered messages found and printed.</returns>
    public async Task<int> InspectAsync(string topicName, string subscriptionName, CancellationToken cancellationToken)
    {
        await using var receiver = _client.CreateReceiver(topicName, subscriptionName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        });

        Console.WriteLine($"[DLQ:{subscriptionName}] Checking dead-letter queue...");

        var messages = await receiver.ReceiveMessagesAsync(maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(10), cancellationToken);

        if (messages.Count == 0)
        {
            Console.WriteLine($"[DLQ:{subscriptionName}] No dead-lettered messages found.");
            return 0;
        }

        foreach (var message in messages)
        {
            Console.WriteLine($"[DLQ:{subscriptionName}] MessageId={message.MessageId}");
            Console.WriteLine($"[DLQ:{subscriptionName}]   DeadLetterReason={message.DeadLetterReason}");
            Console.WriteLine($"[DLQ:{subscriptionName}]   DeadLetterErrorDescription={message.DeadLetterErrorDescription}");
            Console.WriteLine($"[DLQ:{subscriptionName}]   DeliveryCount={message.DeliveryCount}");
            Console.WriteLine($"[DLQ:{subscriptionName}]   Body={message.Body}");

            // Complete on the DLQ sub-queue = permanently remove it from the DLQ now that we've
            // captured/logged the evidence. (Leave it in place instead if you want to re-inspect later.)
            await receiver.CompleteMessageAsync(message, cancellationToken);
        }

        return messages.Count;
    }
}
