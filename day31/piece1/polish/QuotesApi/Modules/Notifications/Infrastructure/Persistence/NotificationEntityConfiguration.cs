using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Notifications.Domain;

namespace QuotesApi.Modules.Notifications.Infrastructure.Persistence;

public sealed class NotificationEntityConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.ToTable("Notifications");

        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id)
            .ValueGeneratedNever();

        entity.Property(x => x.UserId)
            .IsRequired();
        entity.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(Notification.MaxTypeLength);
        entity.Property(x => x.Message)
            .IsRequired()
            .HasMaxLength(Notification.MaxMessageLength);
        entity.Property(x => x.IsRead)
            .IsRequired();
        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();
        entity.Property(x => x.SourceMessageId)
            .IsRequired();

        // Durable idempotency: one Service Bus MessageId can only ever produce one row, even
        // when two competing deliveries of the same message race each other. The consumer
        // treats the resulting unique-key violation as "already processed".
        entity.HasIndex(x => x.SourceMessageId)
            .IsUnique();

        // Serves GET /api/v1/notifications (newest first, per user).
        entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });

        // No FK to Users: Notifications depends on Shared only and never on Identity's types,
        // the same boundary OutboxMessage already follows.
    }
}
