using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Modules.Quotes.Application;

public interface ICollectionRepository
{
    Task<Collection?> GetById(int id, CancellationToken cancellationToken);

    /// <summary>All collections owned by the given user, each with its items loaded.</summary>
    Task<IReadOnlyList<Collection>> GetByOwnerId(int ownerId, CancellationToken cancellationToken);

    /// <summary>The owner's existing collection with this exact (trimmed) name, if any — used to
    /// enforce "one Favorites per user" without relying solely on the DB unique index.</summary>
    Task<Collection?> FindByOwnerAndName(int ownerId, string name, CancellationToken cancellationToken);

    Task Add(Collection collection, CancellationToken cancellationToken);
    Task Update(Collection collection, CancellationToken cancellationToken);
    Task Delete(Collection collection, CancellationToken cancellationToken);
}