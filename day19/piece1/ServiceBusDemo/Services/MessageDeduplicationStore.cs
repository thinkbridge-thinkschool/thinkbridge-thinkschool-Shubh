using System.Collections.Concurrent;

namespace ServiceBusDemo.Services;

/// <summary>
/// Thread-safe, in-memory idempotency store keyed by Service Bus MessageId.
///
/// MessageId is the right dedup key because the publisher assigns it deliberately and
/// deterministically per logical event (see ServiceBusPublisher) — unlike Service Bus's own
/// internal SequenceNumber (which is always unique, even for re-sends of "the same" business
/// event) or LockToken (which changes on every delivery attempt of the SAME message).
/// MessageId is the one identifier that stays stable both when Service Bus redelivers a message
/// after an Abandon/lock-expiry AND when a producer intentionally re-publishes the same logical
/// event (e.g. after a retry on the publish side).
///
/// The key is scoped per-subscription ("subscriptionName:messageId") because subscription A and
/// subscription B are independent logical consumers that must each process their own copy of a
/// topic message exactly once — a message being processed on subscription A is not a "duplicate"
/// from subscription B's point of view.
///
/// This store is process-local/in-memory, which is sufficient to demonstrate the pattern for this
/// exercise. In production with multiple consumer processes/machines, this would need to be a
/// shared, persistent store (e.g. a database unique constraint or a Redis SETNX) so all instances
/// see the same dedup state.
/// </summary>
public class MessageDeduplicationStore
{
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

    /// <summary>
    /// Atomically reserves a MessageId for processing on a given subscription. Returns true the
    /// first time (caller should go ahead and process the message), false if it's already
    /// reserved/completed by another delivery (caller should skip it as a duplicate).
    ///
    /// Call <see cref="Release"/> if processing then FAILS, so a genuine Service Bus redelivery
    /// (after Abandon) gets a fresh chance to process instead of being mistaken for a duplicate
    /// and completed without ever actually succeeding or exhausting retries into the DLQ.
    /// Do NOT call Release after a successful completion — the reservation should stick, so a
    /// truly duplicate copy of the message (e.g. re-published with the same MessageId) is skipped.
    /// </summary>
    public bool TryReserve(string subscriptionName, string messageId)
    {
        var key = BuildKey(subscriptionName, messageId);
        return _processedKeys.TryAdd(key, 0);
    }

    /// <summary>Releases a reservation after a failed processing attempt so it can be retried.</summary>
    public void Release(string subscriptionName, string messageId)
    {
        var key = BuildKey(subscriptionName, messageId);
        _processedKeys.TryRemove(key, out _);
    }

    private static string BuildKey(string subscriptionName, string messageId) => $"{subscriptionName}:{messageId}";

    public int ProcessedCount => _processedKeys.Count;
}
