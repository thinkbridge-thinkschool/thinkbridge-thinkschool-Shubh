using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Domain;
using QuotesApi.Shared.Contracts;
using QuotesApi.Shared.Events;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Quotes.Infrastructure;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _db;
    private readonly IIntegrationEventWriter _eventWriter;

    public QuoteRepository(QuotesDbContext db, IIntegrationEventWriter eventWriter)
    {
        _db = db;
        _eventWriter = eventWriter;
    }

   public async Task<List<Quote>> GetAllAsync(
       int page,
       int size,
       CancellationToken cancellationToken)
   {
       return await _db.Set<Quote>()
           .AsNoTracking()
           .Skip((page - 1) * size)
           .Take(size)
           .ToListAsync(cancellationToken);
   }

   public async Task<Quote?> GetByIdAsync(
       int id,
       CancellationToken cancellationToken)
   {
       return await _db.Set<Quote>()
           .AsNoTracking()
           .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
   }

        public async Task<Quote> AddAsync(
            Quote quote,
            CancellationToken cancellationToken)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(cancellationToken);

            _db.Set<Quote>().Add(quote);
            await _db.SaveChangesAsync(cancellationToken);

            // Published as a Shared contract, not a direct write to Notifications' outbox
            // table: this is the module boundary from section 8 — Quotes never touches
            // Notifications' internal OutboxMessage type. IIntegrationEventWriter is a
            // scoped service resolving to the SAME QuotesDbContext instance as this
            // repository, so its SaveChangesAsync below is enlisted in the transaction
            // started above and commits atomically with the quote insert.
            var quoteCreated = new QuoteCreatedEvent(
                quote.Id,
                quote.Author,
                quote.Text,
                quote.UserId,
                DateTime.UtcNow);
            await _eventWriter.WriteAsync(
                "QuoteCreated",
                quoteCreated,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return quote;
        }
   public async Task<bool> DeleteAsync(
       int id,
       CancellationToken cancellationToken)
   {
       var quote = await _db.Set<Quote>()
           .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

       if (quote is null)
           return false;

       quote.SoftDelete();
       await _db.SaveChangesAsync(cancellationToken);

       return true;
   }
}
