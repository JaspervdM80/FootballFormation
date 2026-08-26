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
    }
}
