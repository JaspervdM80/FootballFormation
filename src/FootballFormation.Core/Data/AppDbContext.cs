using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonSquadMember> SeasonSquadMembers => Set<SeasonSquadMember>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePeriod> GamePeriods => Set<GamePeriod>();
    public DbSet<GamePlayerPosition> GamePlayerPositions => Set<GamePlayerPosition>();
    public DbSet<GameGoal> GameGoals => Set<GameGoal>();
    public DbSet<GameSubstitution> GameSubstitutions => Set<GameSubstitution>();
    public DbSet<MatchPreferences> MatchPreferences => Set<MatchPreferences>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

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

        modelBuilder.Entity<Season>(entity =>
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
        });

        modelBuilder.Entity<SeasonSquadMember>(entity =>
        {
            entity.HasKey(m => m.Id);

            // Cascade on both sides, unlike Season -> Game. A membership row carries no history —
            // it is purely "is this person in that squad" — so it must never block deleting the
            // person or an (already game-free) season, and an orphan row would be meaningless.
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
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Opponent).IsRequired().HasMaxLength(100);
            UseCsvIntList(entity.Property(g => g.UnavailablePlayerIds));
            UseCsvIntList(entity.Property(g => g.GuestPlayerIds));
            entity.HasMany(g => g.Periods)
                .WithOne(p => p.Game)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(g => g.Goals)
                .WithOne(gl => gl.Game)
                .HasForeignKey(gl => gl.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(g => g.Substitutions)
                .WithOne(s => s.Game)
                .HasForeignKey(s => s.GameId)
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
        });

        modelBuilder.Entity<GameSubstitution>(entity =>
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
        });

        modelBuilder.Entity<MatchPreferences>(entity =>
        {
            entity.HasKey(m => m.Id);

            // Cascade, like SeasonSquadMember and unlike Season -> Game: a preferences row is
            // pure configuration with no history of its own, so it must not make an otherwise
            // game-free season undeletable.
            entity.HasOne(m => m.Season)
                .WithMany()
                .HasForeignKey(m => m.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per season. MatchPreferencesService creates it on first read; this is the
            // net underneath two circuits reading the same season at once.
            entity.HasIndex(m => m.SeasonId).IsUnique();
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Username).IsRequired().HasMaxLength(50);
            entity.Property(a => a.PasswordHash).IsRequired();
        });
    }

    /// <summary>Stores a List&lt;int&gt; as comma-separated text, with the comparer EF needs to detect changes.</summary>
    private static void UseCsvIntList(PropertyBuilder<List<int>> property) =>
        property.HasConversion(
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
}
