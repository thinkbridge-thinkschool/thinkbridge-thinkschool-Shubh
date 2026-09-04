using QuoteManagement.Modules.Quotes.Domain;

namespace QuoteManagement.Modules.Quotes.Application;

// Application depends on this abstraction, never on QuotesDbContext or EF Core directly.
// Infrastructure provides the real implementation; this keeps the persistence technology
// swappable and keeps the domain/application layers testable without a database.
internal interface IQuoteRepository
{
    void Add(Quote quote);
    Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Quote>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
