using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Identity.Domain;

namespace QuotesApi.Modules.Identity.Infrastructure.Persistence;

public sealed class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.ToTable("RefreshTokens");

        entity.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.Property(x => x.Token)
            .HasMaxLength(450)
            .IsRequired();

        entity.Property(x => x.ReplacedByToken)
            .HasMaxLength(450);

        entity.HasIndex(x => x.Token)
            .IsUnique();
    }
}
