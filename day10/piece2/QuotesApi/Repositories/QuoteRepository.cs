using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _db;

    public QuoteRepository(QuotesDbContext db)
    {
        _db = db;
    }

   public async Task<List<Quote>> GetAllAsync(
       int page,
       int size,
       CancellationToken cancellationToken)
   {
       return await _db.Quotes
           .AsNoTracking()
           .Skip((page - 1) * size)
           .Take(size)
           .ToListAsync(cancellationToken);
   }

   public async Task<Quote?> GetByIdAsync(
       int id,
       CancellationToken cancellationToken)
   {
       return await _db.Quotes
           .AsNoTracking()
           .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
   }

   public async Task<Quote> AddAsync(
       Quote quote,
       CancellationToken cancellationToken)
   {
       _db.Quotes.Add(quote);
       await _db.SaveChangesAsync(cancellationToken);
       return quote;
   }

   public async Task<bool> DeleteAsync(
       int id,
       CancellationToken cancellationToken)
   {
       var quote = await _db.Quotes
           .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, cancellationToken);

       if (quote is null)
           return false;

       quote.SoftDelete();
       await _db.SaveChangesAsync(cancellationToken);

       return true;
   }
}