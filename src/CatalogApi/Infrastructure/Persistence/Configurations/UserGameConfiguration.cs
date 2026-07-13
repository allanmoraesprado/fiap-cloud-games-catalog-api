using CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogApi.Infrastructure.Persistence.Configurations;

public class UserGameConfiguration : IEntityTypeConfiguration<UserGame>
{
    public void Configure(EntityTypeBuilder<UserGame> b)
    {
        b.ToTable("user_games");
        b.HasKey(x => x.Id);
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.GameId).IsRequired();
        // Uniqueness makes the future approved-payment library insert idempotent.
        b.HasIndex(x => new { x.UserId, x.GameId }).IsUnique();
    }
}
