using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GameGoalConfiguration : IEntityTypeConfiguration<GameGoal>
{
    public void Configure(EntityTypeBuilder<GameGoal> entity)
    {
        entity.HasKey(g => g.Id);

        // SetNull, not Cascade: a deleted player must not take the goal — and with it the scoreline — out of the record.
        entity.HasOne(g => g.Scorer)
            .WithMany()
            .HasForeignKey(g => g.ScorerId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(g => g.Assister)
            .WithMany()
            .HasForeignKey(g => g.AssisterId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade: a goal pointing at a line-up that no longer exists has no minute to show. No navigation — see GameGoal.GamePeriodId.
        entity.HasOne<GamePeriod>()
            .WithMany()
            .HasForeignKey(g => g.GamePeriodId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GameSubstitutionConfiguration : IEntityTypeConfiguration<GameSubstitution>
{
    public void Configure(EntityTypeBuilder<GameSubstitution> entity)
    {
        entity.HasKey(s => s.Id);

        entity.HasOne(s => s.GamePeriod)
            .WithMany()
            .HasForeignKey(s => s.GamePeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on both legs: neither is nullable, and two cascade paths from Players to one row is the shape SQLite rejects outright.
        // Deleting a substituted player therefore fails loudly rather than silently rewriting match history.
        entity.HasOne(s => s.PlayerOff)
            .WithMany()
            .HasForeignKey(s => s.PlayerOffId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(s => s.PlayerOn)
            .WithMany()
            .HasForeignKey(s => s.PlayerOnId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GameInjuryConfiguration : IEntityTypeConfiguration<GameInjury>
{
    public void Configure(EntityTypeBuilder<GameInjury> entity)
    {
        entity.HasKey(i => i.Id);

        // Both legs declared without a navigation — see GameInjury, which carries the ids alone.
        entity.HasOne<GamePeriod>()
            .WithMany()
            .HasForeignKey(i => i.GamePeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: deleting someone hurt in a match fails loudly rather than silently rewriting how long she was available for it.
        entity.HasOne<Player>()
            .WithMany()
            .HasForeignKey(i => i.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One per player per match: she does not come back, so a second row is only ever a double tap — and AvailableMinutesFor reads
        // the first.
        entity.HasIndex(i => new { i.GameId, i.PlayerId }).IsUnique();
    }
}
