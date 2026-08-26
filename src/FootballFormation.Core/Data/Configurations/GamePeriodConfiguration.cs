using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GamePeriodConfiguration : IEntityTypeConfiguration<GamePeriod>
{
    public void Configure(EntityTypeBuilder<GamePeriod> entity)
    {
        entity.HasKey(p => p.Id);

        entity.HasMany(p => p.PlayerPositions)
            .WithOne(pp => pp.GamePeriod)
            .HasForeignKey(pp => pp.GamePeriodId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GamePlayerPositionConfiguration : IEntityTypeConfiguration<GamePlayerPosition>
{
    public void Configure(EntityTypeBuilder<GamePlayerPosition> entity)
    {
        entity.HasKey(pp => pp.Id);

        entity.HasOne(pp => pp.Player)
            .WithMany()
            .HasForeignKey(pp => pp.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // A player appears once per period — on the pitch or on the bench, never both. The code assumed it all along but nothing
        // enforced it, which is why PlannedChangesReport still defends with TryAdd against line-ups an older build wrote.
        entity.HasIndex(pp => new { pp.GamePeriodId, pp.PlayerId }).IsUnique();
    }
}
