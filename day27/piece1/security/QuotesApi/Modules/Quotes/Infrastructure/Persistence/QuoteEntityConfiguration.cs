using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Modules.Quotes.Infrastructure.Persistence;

// Quotes owns its own EF Core mapping. The Shared QuotesDbContext discovers this via
// ModelBuilder.ApplyConfigurationsFromAssembly over every loaded assembly (see
// Shared/Infrastructure/Persistence/QuotesDbContext.cs) — Shared never references this
// module's types directly, keeping the dependency direction Modules -> Shared only.
public sealed class QuoteEntityConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> entity)
    {
        // Without a DbSet<Quote> property on QuotesDbContext (see Shared's DbContext for
        // why), EF Core's default table-naming convention would use the entity's own CLR
        // name ("Quote") instead of the plural table the existing migrations already
        // created — this pins it back to the real table.
        entity.ToTable("Quotes");

        entity.HasKey(q => q.Id);

        entity.Property(q => q.Author)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(q => q.Text)
            .IsRequired()
            .HasMaxLength(1000);

        entity.Property(q => q.IsDeleted)
            .IsRequired();

        entity.HasQueryFilter(q => !q.IsDeleted);
    }
}
