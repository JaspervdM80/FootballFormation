using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GameGoalConfiguration : IEntityTypeConfiguration<GameGoal>
{
    public void Configure(EntityTypeBuilder<GameGoal> entity)
    {
        entity.HasKey(g => g.Id);

        // SetNull, not Cascade: ScorerId is nullable now that opponent goals are tracked, and a
        // deleted player must not take the goal (and with it the scoreline) out of the record.
        entity.HasOne(g => g.Scorer)
            .WithMany()
            .HasForeignKey(g => g.ScorerId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(g => g.Assister)
            .WithMany()
            .HasForeignKey(g => g.AssisterId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade like GameSubstitution's: the half and the events recorded during it are one
        // record, and a goal pointing at a line-up that no longer exists has no minute to show.
        // Declared without a navigation — see GameGoal.GamePeriodId.
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

        // Restrict on both player legs. They are not nullable — a substitution without either
        // side is meaningless — and two cascading paths from Players to the same row is exactly
        // the multiple-cascade-path shape SQLite rejects. Deleting a player who was substituted
        // therefore fails loudly rather than silently rewriting match history.
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

        // Restrict, like GameSubstitution's player legs: deleting someone who was hurt in a match
        // fails loudly rather than silently rewriting how long she was available for it.
        entity.HasOne<Player>()
            .WithMany()
            .HasForeignKey(i => i.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One injury per player per match. She leaves the pitch for it and does not come back, so
        // a second row would only ever be a double tap — and AvailableMinutesFor reads the first.
        entity.HasIndex(i => new { i.GameId, i.PlayerId }).IsUnique();
    }
}
