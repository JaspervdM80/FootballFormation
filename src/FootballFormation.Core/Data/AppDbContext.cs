using Microsoft.EntityFrameworkCore.Diagnostics;

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
    public DbSet<GameInjury> GameInjuries => Set<GameInjury>();
    public DbSet<GameComment> GameComments => Set<GameComment>();
    public DbSet<Training> Trainings => Set<Training>();
    public DbSet<MatchPreferences> MatchPreferences => Set<MatchPreferences>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Team> Teams => Set<Team>();

    /// The team and club this context is scoped to, stamped by TeamScopedDbContextFactory before any query runs. Null on a context the
    /// factory did not stamp — which the filters below read as "match nothing", so a query that escapes the factory fails closed rather
    /// than leaking another team's data. The boot steps that must read across teams stamp a team by hand. Virtual so a test context can
    /// track the fake team in scope live, rather than restamping every context each time a test switches teams.
    public virtual int? CurrentTeamId { get; set; }
    public virtual int? CurrentClubId { get; set; }

    /// The season-scoped roots carry the team filter; their child navigations (a game's goals, comments, periods) reach the team only
    /// through an already-filtered parent, never queried across teams on their own. EF cannot see that and warns that both ends should
    /// filter — silenced so a boot log stays about real problems. See docs/known_issues/ef-core.md.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    /// Each entity's mapping lives beside it in Data/Configurations — the delete behaviours especially are reasoned per entity, and are
    /// far easier to review one aggregate at a time. See docs/models/enums-and-relationships.md.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // The team scope, applied here rather than in the per-entity configs because a filter reads this context instance's CurrentTeamId
        // and a config only gets a builder. EF re-evaluates the member per query, so one stamped context serves the whole render. See
        // docs/patterns/authorization-and-auth.md.
        modelBuilder.Entity<Season>().HasQueryFilter(s => s.TeamId == CurrentTeamId);
        modelBuilder.Entity<Game>().HasQueryFilter(g => g.TeamId == CurrentTeamId);
        modelBuilder.Entity<Training>().HasQueryFilter(t => t.TeamId == CurrentTeamId);
        modelBuilder.Entity<MatchPreferences>().HasQueryFilter(m => m.TeamId == CurrentTeamId);
        modelBuilder.Entity<SeasonSquadMember>().HasQueryFilter(m => m.TeamId == CurrentTeamId);

        // Players belong to the club, not the team: a season's squad draws from the club pool, so a girl who moves between teams keeps one row.
        modelBuilder.Entity<Player>().HasQueryFilter(p => p.ClubId == CurrentClubId);
    }
}
