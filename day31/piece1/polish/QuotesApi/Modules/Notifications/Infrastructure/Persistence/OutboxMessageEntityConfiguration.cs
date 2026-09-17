using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Notifications.Domain;

namespace QuotesApi.Modules.Notifications.Infrastructure.Persistence;

public sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("OutboxMessages");

        entity.HasKey(x => x.Id);
        entity.Property(x => x.MessageType)
            .IsRequired()
            .HasMaxLength(200);
        entity.Property(x => x.Payload)
            .IsRequired();
        entity.Property(x => x.OccurredOnUtc)
            .IsRequired();
        entity.HasIndex(x => x.ProcessedOnUtc);
    }
}
