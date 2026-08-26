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

    /// Each entity's mapping lives beside it in Data/Configurations — the delete behaviours especially are reasoned per entity, and are
    /// far easier to review one aggregate at a time. See docs/models/enums-and-relationships.md.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
