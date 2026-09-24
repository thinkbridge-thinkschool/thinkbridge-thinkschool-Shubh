namespace QuotesApi.Modules.Notifications.Application;

/// <summary>
/// Settings for <see cref="QuotesApi.Modules.Notifications.Infrastructure.NotificationsConsumerWorker"/>.
/// Bound from the "ServiceBus:Notifications" configuration section; the namespace and topic
/// themselves are reused from <see cref="ServiceBusOptions"/>.
/// </summary>
public record NotificationConsumerOptions
{
    /// <summary>
    /// Dedicated subscription on the topic, owned by this consumer alone (not the Day 19 demo
    /// subscriptions sub-a / sub-b, which may hold older messages from other days' databases).
    /// </summary>
    public string SubscriptionName { get; init; } = string.Empty;

    /// <summary>How many messages the processor handles in parallel.</summary>
    public int MaxConcurrentCalls { get; init; } = 4;
}
