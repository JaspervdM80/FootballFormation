using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> entity)
    {
        entity.HasKey(s => s.Id);
        entity.Property(s => s.Name).IsRequired().HasMaxLength(20);

        // One season per start date per team — two teams may both run a 2025/26 window, but one team may not have it twice. Stops a
        // double-create on /settings producing duplicates, and is the safety net if the season-derivation rule is ever changed.
        entity.HasIndex(s => new { s.TeamId, s.StartDate }).IsUnique();

        // Restrict, as Club -> Team: a season is the root of a team's data, and deleting the team must never take a year of it. Id only,
        // no navigation, like AppUser -> Team.
        entity.HasOne<Team>()
            .WithMany()
            .HasForeignKey(s => s.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: deleting a season must never take a year of games, lineups
        // and goals with it. SeasonService.DeleteAsync refuses with a readable message instead.
        entity.HasMany(s => s.Games)
            .WithOne(g => g.Season)
            .HasForeignKey(g => g.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
