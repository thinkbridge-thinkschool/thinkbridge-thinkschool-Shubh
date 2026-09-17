using System.Text.Json;
using QuotesApi.Modules.Notifications.Domain;
using QuotesApi.Shared.Contracts;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Notifications.Infrastructure;

// Notifications' side of the Shared IIntegrationEventWriter contract: this is the ONLY
// place an OutboxMessage row gets created. Registered scoped, so within one HTTP request it
// resolves to the same QuotesDbContext instance as the caller (e.g. Quotes' QuoteRepository),
// which is what lets its SaveChangesAsync join the caller's ambient transaction instead of
// needing a distributed transaction across modules.
public sealed class OutboxEventWriter : IIntegrationEventWriter
{
    private readonly QuotesDbContext _db;

    public OutboxEventWriter(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync<TEvent>(
        string messageType,
        TEvent payload,
        CancellationToken cancellationToken)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = messageType,
            Payload = JsonSerializer.Serialize(payload),
            OccurredOnUtc = DateTime.UtcNow
        };

        _db.Set<OutboxMessage>().Add(message);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
