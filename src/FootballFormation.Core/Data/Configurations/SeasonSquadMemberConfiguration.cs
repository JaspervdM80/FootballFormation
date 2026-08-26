using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class SeasonSquadMemberConfiguration : IEntityTypeConfiguration<SeasonSquadMember>
{
    public void Configure(EntityTypeBuilder<SeasonSquadMember> entity)
    {
        entity.HasKey(m => m.Id);

        // Cascade on both sides, unlike Season -> Game: a membership row carries no history of its own, so it must never block deleting
        // the person or an already game-free season.
        entity.HasOne(m => m.Season)
            .WithMany(s => s.SquadMembers)
            .HasForeignKey(m => m.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(m => m.Player)
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per player per season. SeasonSquadService refuses duplicates with a readable
        // message; this is the net underneath it.
        entity.HasIndex(m => new { m.SeasonId, m.PlayerId }).IsUnique();
    }
}
