using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FootballFormation.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePeriod> GamePeriods => Set<GamePeriod>();
    public DbSet<GamePlayerPosition> GamePlayerPositions => Set<GamePlayerPosition>();
    public DbSet<GameGoal> GameGoals => Set<GameGoal>();
    public DbSet<MatchPreferences> MatchPreferences => Set<MatchPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.FirstName).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Surname).HasMaxLength(50);
            entity.Property(p => p.AlternativePositions)
                .HasConversion(
                    v => string.Join(',', v.Select(p => (int)p)),
                    v => v.Length == 0
                        ? new List<PlayerPosition>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => (PlayerPosition)int.Parse(s))
                            .ToList(),
                    new ValueComparer<List<PlayerPosition>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToList()));
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Opponent).IsRequired().HasMaxLength(100);
            entity.Property(g => g.UnavailablePlayerIds)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Length == 0
                        ? new List<int>()
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => int.Parse(s))
                            .ToList(),
                    new ValueComparer<List<int>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToList()));
            entity.HasMany(g => g.Periods)
                .WithOne(p => p.Game)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(g => g.Goals)
                .WithOne(gl => gl.Game)
                .HasForeignKey(gl => gl.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GamePeriod>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasMany(p => p.PlayerPositions)
                .WithOne(pp => pp.GamePeriod)
                .HasForeignKey(pp => pp.GamePeriodId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GamePlayerPosition>(entity =>
        {
            entity.HasKey(pp => pp.Id);
            entity.HasOne(pp => pp.Player)
                .WithMany()
                .HasForeignKey(pp => pp.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameGoal>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.HasOne(g => g.Scorer)
                .WithMany()
                .HasForeignKey(g => g.ScorerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(g => g.Assister)
                .WithMany()
                .HasForeignKey(g => g.AssisterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MatchPreferences>(entity =>
        {
            entity.HasKey(m => m.Id);
        });
    }
}
