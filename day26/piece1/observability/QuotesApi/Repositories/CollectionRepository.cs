using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

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
        return await _db.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task Add(
        Collection collection,
        CancellationToken cancellationToken)
    {
        await _db.Collections.AddAsync(collection, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task Update(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Update(collection);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task Delete(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(cancellationToken);
    }
}