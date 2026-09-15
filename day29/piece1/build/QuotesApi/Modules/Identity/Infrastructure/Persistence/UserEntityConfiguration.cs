using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Identity.Domain;

namespace QuotesApi.Modules.Identity.Infrastructure.Persistence;

public sealed class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.ToTable("Users");

        entity.HasKey(u => u.Id);

        entity.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        entity.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(100);

        // Registration checks for an existing email before inserting, but only this
        // index actually prevents two concurrent registrations for the same address
        // from both passing that check and creating duplicate accounts.
        entity.HasIndex(u => u.Email)
            .IsUnique();
    }
}
