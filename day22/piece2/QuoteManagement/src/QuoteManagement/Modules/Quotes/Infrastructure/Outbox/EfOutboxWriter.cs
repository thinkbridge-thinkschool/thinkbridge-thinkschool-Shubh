using System.Text.Json;
using QuoteManagement.Modules.Quotes.Application;
using QuoteManagement.Shared.Application.EventBus;

namespace QuoteManagement.Modules.Quotes.Infrastructure.Outbox;

internal sealed class EfOutboxWriter(QuotesDbContext dbContext) : IOutboxWriter
{
    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : IIntegrationEvent
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = typeof(TEvent).FullName!,
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        });
    }
}
