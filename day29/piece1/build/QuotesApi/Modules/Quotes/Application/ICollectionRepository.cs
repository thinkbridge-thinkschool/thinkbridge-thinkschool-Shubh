using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Modules.Quotes.Application;

public interface ICollectionRepository
{
    Task<Collection?> GetById(int id, CancellationToken cancellationToken);
    Task Add(Collection collection, CancellationToken cancellationToken);
    Task Update(Collection collection, CancellationToken cancellationToken);
    Task Delete(Collection collection, CancellationToken cancellationToken);
}