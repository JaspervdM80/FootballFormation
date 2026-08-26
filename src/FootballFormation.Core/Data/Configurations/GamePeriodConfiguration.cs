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

        // A player appears once per period — on the pitch or on the bench, never both, never twice.
        // The code has assumed this all along (SavePeriodLineupAsync rebuilds the period from
        // scratch, FormationBuilder swaps rather than duplicates), but nothing enforced it, so a
        // lineup written by an older build could break it and PlannedChangesReport had to defend
        // with TryAdd. The index makes the assumption real.
        entity.HasIndex(pp => new { pp.GamePeriodId, pp.PlayerId }).IsUnique();
    }
}
