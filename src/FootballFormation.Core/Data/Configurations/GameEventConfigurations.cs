using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
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
