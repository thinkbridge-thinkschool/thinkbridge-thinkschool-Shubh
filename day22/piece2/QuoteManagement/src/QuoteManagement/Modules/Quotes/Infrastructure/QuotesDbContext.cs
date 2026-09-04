using Microsoft.EntityFrameworkCore;
using QuoteManagement.Modules.Quotes.Application;
using QuoteManagement.Modules.Quotes.Domain;
using QuoteManagement.Modules.Quotes.Infrastructure.Outbox;

namespace QuoteManagement.Modules.Quotes.Infrastructure;

// This module's own DbContext. No other module may reference it, query it, or add a
// migration against it — Identity and Notifications each own their own persistence (in
// this scaffold, simple in-memory stores) and reach Quotes data only through the
// QuoteCreatedIntegrationEvent contract, never through this type.
//
// Uses the EF Core InMemory provider so the whole scaffold runs with zero external
// dependencies (no SQL Server/SQLite setup required); the pattern is identical with a real
// relational provider, as used elsewhere in this repo (see day1/day21 QuotesApi).
internal sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(quote =>
        {
            quote.HasKey(q => q.Id);
            quote.Property(q => q.Author).IsRequired().HasMaxLength(200);
            quote.Property(q => q.Text).IsRequired().HasMaxLength(Quote.MaxTextLength);
            quote.Property(q => q.UserId).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Type).IsRequired();
            outbox.Property(m => m.Payload).IsRequired();
        });
    }

    // Explicit implementation: DbContext already exposes a same-named SaveChangesAsync
    // that returns Task<int>, so IUnitOfWork's Task-returning member is implemented
    // explicitly and reached only through the IUnitOfWork abstraction the application
    // layer actually depends on.
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await base.SaveChangesAsync(cancellationToken);
}
