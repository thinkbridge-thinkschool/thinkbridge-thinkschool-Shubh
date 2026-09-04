using Microsoft.EntityFrameworkCore;
using QuoteManagement.Modules.Quotes.Application;
using QuoteManagement.Modules.Quotes.Domain;

namespace QuoteManagement.Modules.Quotes.Infrastructure;

internal sealed class QuoteRepository(QuotesDbContext dbContext) : IQuoteRepository
{
    public void Add(Quote quote) => dbContext.Quotes.Add(quote);

    public Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Quote>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Quotes
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
