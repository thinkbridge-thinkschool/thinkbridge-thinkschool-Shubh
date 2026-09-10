using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetAllAsync(
    int page,
    int size,
    CancellationToken cancellationToken);
    Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}