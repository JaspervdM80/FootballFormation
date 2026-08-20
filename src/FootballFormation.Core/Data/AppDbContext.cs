using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<MatchPreferences> MatchPreferences => Set<MatchPreferences>();
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>
    /// Each entity's mapping lives beside it in <c>Data/Configurations</c>. The delete behaviours
    /// in particular are deliberate and reasoned per entity (see docs/models/enums-and-relationships.md), and they are far
    /// easier to review one aggregate at a time than as one long method.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
