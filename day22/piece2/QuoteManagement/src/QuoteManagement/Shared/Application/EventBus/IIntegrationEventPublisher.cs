namespace QuoteManagement.Shared.Application.EventBus;

// A publishing module (e.g. Quotes' outbox relay) depends only on this interface, never on
// how events actually get delivered. In this scaffold the implementation is in-process
// (Shared.Infrastructure.InProcessIntegrationEventDispatcher); in production it could be
// swapped for a real broker (Service Bus, RabbitMQ) without changing any module.
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}
