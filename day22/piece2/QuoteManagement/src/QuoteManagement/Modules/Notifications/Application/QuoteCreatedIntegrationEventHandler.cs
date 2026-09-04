using QuoteManagement.Modules.Notifications.Domain;
using QuoteManagement.Shared.Application.EventBus;
using QuoteManagement.Shared.Contracts.Quotes;

namespace QuoteManagement.Modules.Notifications.Application;

// Flow 2's entry point: reacts to the QuoteCreatedIntegrationEvent published by the Quotes
// module's outbox relay. This is the only line connecting Notifications to Quotes, and it
// runs through the shared contract, not a direct call — Notifications never touches a
// Quotes type.
//
// Handling is written to be idempotent-safe: creating a notification from the same event
// twice (e.g. after an outbox redelivery) just creates two notification rows in this
// scaffold, which is an acceptable simplification for a design/scaffold exercise — a full
// implementation would de-duplicate on EventId.
internal sealed class QuoteCreatedIntegrationEventHandler(
    INotificationRepository repository,
    INotificationSender sender,
    TimeProvider timeProvider) : IIntegrationEventHandler<QuoteCreatedIntegrationEvent>
{
    public async Task HandleAsync(QuoteCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(
            integrationEvent.UserId,
            $"Your quote by {integrationEvent.Author} was published.",
            timeProvider.GetUtcNow());

        repository.Add(notification);
        await sender.SendAsync(notification, cancellationToken);
        notification.MarkSent(timeProvider.GetUtcNow());
    }
}
