using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> entity)
    {
        entity.HasKey(p => p.Id);
        entity.Property(p => p.FirstName).IsRequired().HasMaxLength(50);
        entity.Property(p => p.Surname).HasMaxLength(50);
        entity.Property(p => p.AlternativePositions).HasCsvListConversion();

        // Restrict, as Club -> Team: a player is club history, and deleting the club must not take it. Id only, no navigation.
        entity.HasOne<Club>()
            .WithMany()
            .HasForeignKey(p => p.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
