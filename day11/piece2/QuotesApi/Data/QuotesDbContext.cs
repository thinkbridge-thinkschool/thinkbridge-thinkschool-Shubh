using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
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
            // Day 11 - Index for quote lookups by user
            entity.HasIndex(q => q.UserId); 
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(c => c.OwnerId)
                .IsRequired();

            entity.OwnsMany(c => c.Items, item =>
            {
                item.WithOwner()
                    .HasForeignKey("CollectionId");

                item.HasKey("CollectionId", "QuoteId");

                item.Property(i => i.QuoteId)
                    .ValueGeneratedNever()
                    .IsRequired();

                item.Property(i => i.AddedAt)
                    .IsRequired();
            });
        });

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(320);

            entity.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(100);
        });

        // RefreshToken -> User relationship
        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.Property(x => x.Token)
                .HasMaxLength(450)
                .IsRequired();

            entity.Property(x => x.ReplacedByToken)
                .HasMaxLength(450);

            entity.HasIndex(x => x.Token)
                .IsUnique();
        });
    }
}