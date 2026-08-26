using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> entity)
    {
        entity.HasKey(s => s.Id);
        entity.Property(s => s.Name).IsRequired().HasMaxLength(20);

        // One season per start date — stops a double-create on /settings producing duplicates,
        // and is the safety net if the season-derivation rule is ever changed.
        entity.HasIndex(s => s.StartDate).IsUnique();

        // Restrict, not Cascade: deleting a season must never take a year of games, lineups
        // and goals with it. SeasonService.DeleteAsync refuses with a readable message instead.
        entity.HasMany(s => s.Games)
            .WithOne(g => g.Season)
            .HasForeignKey(g => g.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
