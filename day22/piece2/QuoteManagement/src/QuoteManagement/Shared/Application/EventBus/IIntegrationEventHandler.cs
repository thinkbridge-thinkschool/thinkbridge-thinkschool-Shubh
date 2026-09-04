namespace QuoteManagement.Shared.Application.EventBus;

// A consuming module (e.g. Notifications) implements this for events it cares about and
// registers it in its own DI extension. It never subscribes to the publishing module's
// internal types — only to the shared contract.
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
