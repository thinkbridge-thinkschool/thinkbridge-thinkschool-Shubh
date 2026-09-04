using Microsoft.Extensions.DependencyInjection;
using QuoteManagement.Shared.Application.EventBus;

namespace QuoteManagement.Shared.Infrastructure;

// Stands in for a real message broker (Azure Service Bus, RabbitMQ, ...) for this scaffold:
// it resolves whichever module(s) registered a handler for this event type via DI and
// invokes them in-process, in a fresh scope per event. A production system would swap this
// for a real broker client behind the same IIntegrationEventPublisher interface — no
// module's code would need to change, because publishers and handlers only ever depend on
// the interfaces in Shared.Application.EventBus.
public sealed class InProcessIntegrationEventDispatcher(IServiceProvider serviceProvider) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        using var scope = serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(integrationEvent, cancellationToken);
        }
    }
}
