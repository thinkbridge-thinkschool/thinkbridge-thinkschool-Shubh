using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Modules.Quotes.Infrastructure.Persistence;

public sealed class CollectionEntityConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> entity)
    {
        entity.ToTable("Collections");

        entity.HasKey(c => c.Id);

        entity.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(80);

        entity.Property(c => c.OwnerId)
            .IsRequired();

        // Day 30: a user must not be able to end up with two "Favorites" collections — the
        // application layer already checks for an existing name before creating one
        // (CollectionRepository.FindByOwnerAndName), but a unique index is the real guarantee
        // under concurrent requests (two near-simultaneous "New Collection: Favorites" calls).
        entity.HasIndex(c => new { c.OwnerId, c.Name })
            .IsUnique();

        entity.OwnsMany(c => c.Items, item =>
        {
            item.ToTable("CollectionItem");

            item.WithOwner()
                .HasForeignKey("CollectionId");

            item.HasKey("CollectionId", "QuoteId");

            item.Property(i => i.QuoteId)
                .ValueGeneratedNever()
                .IsRequired();

            item.Property(i => i.AddedAt)
                .IsRequired();
        });
    }
}
