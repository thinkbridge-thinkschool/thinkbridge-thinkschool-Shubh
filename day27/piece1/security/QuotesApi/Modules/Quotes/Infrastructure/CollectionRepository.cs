using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Domain;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Quotes.Infrastructure;

public class CollectionRepository : ICollectionRepository
{
    private readonly QuotesDbContext _db;

    public CollectionRepository(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<Collection?> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        return await _db.Set<Collection>()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task Add(
        Collection collection,
        CancellationToken cancellationToken)
    {
        await _db.Set<Collection>().AddAsync(collection, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task Update(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Set<Collection>().Update(collection);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task Delete(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Set<Collection>().Remove(collection);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
