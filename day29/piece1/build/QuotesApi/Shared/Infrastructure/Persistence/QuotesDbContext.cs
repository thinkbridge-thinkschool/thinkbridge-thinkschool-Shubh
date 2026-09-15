using Microsoft.EntityFrameworkCore;

namespace QuotesApi.Shared.Infrastructure.Persistence;

// Shared plumbing, not shared business logic: this class owns the physical connection and
// migration history (keeping "one database" true, per Day 27's instruction not to redesign
// it), but it has ZERO compile-time knowledge of Quote, User, RefreshToken, or OutboxMessage.
// Each module owns its own IEntityTypeConfiguration<T> classes under its own
// Infrastructure/Persistence folder; ApplyConfigurationsFromAssembly discovers them at
// runtime from every assembly the Host has already loaded, so Shared never needs a
// ProjectReference to a module (which would invert the Modules -> Shared dependency
// direction). Repositories reach their own table via Set<TEntity>() rather than a DbSet
// property here, for the same reason.
public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions<QuotesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }
}
