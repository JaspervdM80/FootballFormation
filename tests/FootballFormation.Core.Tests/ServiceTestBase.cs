using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FootballFormation.Core.Tests;

/// A real SQLite database per test, held open in memory for the life of the connection.
public abstract class ServiceTestBase : IDisposable
{
    protected static readonly DateTime Now = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;

    protected ServiceTestBase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        StatsCache = new StatsCache(new MemoryCache(new MemoryCacheOptions()));

        DbFactory = new TestDbContextFactory(_connection, new StatsCacheInvalidator(StatsCache));
        Db = DbFactory.CreateDbContext();
        Db.Database.EnsureCreated();

        Time = new FakeTimeProvider(Now);

        Players = new PlayerService(DbFactory, CurrentUser, NullLogger<PlayerService>.Instance);
        Seasons = new SeasonService(DbFactory, Time, CurrentUser, NullLogger<SeasonService>.Instance);
        Squads = new SeasonSquadService(DbFactory, CurrentUser, NullLogger<SeasonSquadService>.Instance);
        Games = new GameService(DbFactory, Seasons, CurrentUser, Time, NullLogger<GameService>.Instance);
        Trainings = new TrainingService(DbFactory, Seasons, Time, CurrentUser, NullLogger<TrainingService>.Instance);
        Preferences = new MatchPreferencesService(DbFactory, Time, CurrentUser, NullLogger<MatchPreferencesService>.Instance);
        Live = new LiveMatchService(DbFactory, Time, NullLogger<LiveMatchService>.Instance);

        MatchClock = new MatchClockService(DbFactory, Notifier, Time, CurrentUser, NullLogger<MatchClockService>.Instance);
        Goals = new MatchGoalService(DbFactory, Games, Notifier, Time, CurrentUser, NullLogger<MatchGoalService>.Instance);
        Subs = new MatchSubstitutionService(DbFactory, Notifier, Time, CurrentUser, NullLogger<MatchSubstitutionService>.Instance);

        Users = new UserService(DbFactory, CurrentUser, CurrentTeam, NullLogger<UserService>.Instance);
        TeamsAndClubs = new TeamService(DbFactory, CurrentUser, CurrentTeam, NullLogger<TeamService>.Instance);

        Stats = new StatsService(Games, Squads, Trainings, Time, StatsCache, NullLogger<StatsService>.Instance);
    }

    /// An admin by default, so a test about something else does not have to say so.
    protected FakeCurrentUser CurrentUser { get; } = new();

    /// The team in scope. <see cref="SeedTeamAsync"/> points it at what it seeded; a test about something else leaves it unset.
    protected FakeCurrentTeam CurrentTeam { get; } = new();

    /// For arranging and asserting. The services use their own, as in production.
    protected AppDbContext Db { get; }

    protected IDbContextFactory<AppDbContext> DbFactory { get; }
    protected FakeTimeProvider Time { get; }

    protected PlayerService Players { get; }
    protected SeasonService Seasons { get; }
    protected SeasonSquadService Squads { get; }
    protected GameService Games { get; }
    protected TrainingService Trainings { get; }
    protected MatchPreferencesService Preferences { get; }

    /// Shared by the three services below, the way the singleton is in the app. Subscribe to see what a spectator's screen would be told.
    protected LiveMatchNotifier Notifier { get; } = new();

    /// Reading a live match. The three below are how one is written to.
    protected LiveMatchService Live { get; }

    /// The match clock, not the <see cref="TimeProvider"/> driving it — that is <see cref="Time"/>.
    protected MatchClockService MatchClock { get; }

    protected MatchGoalService Goals { get; }
    protected MatchSubstitutionService Subs { get; }
    protected UserService Users { get; }

    /// Named for both, because the page and the service cover the club above the team as well.
    protected TeamService TeamsAndClubs { get; }

    // StatsCache.Generation is how a test asks whether a write was noticed, without timing.
    protected StatsService Stats { get; }

    protected StatsCache StatsCache { get; }

    protected AppDbContext Read() => DbFactory.CreateDbContext();

    protected async Task<Season> SeedSeasonAsync(DateTime? covering = null, bool isCurrent = true)
    {
        var season = Season.CreateFor(covering ?? Now);
        season.IsCurrent = isCurrent;

        Db.Seasons.Add(season);
        await Db.SaveChangesAsync();
        return season;
    }

    /// A club with one team, put in scope — the shape every deployment boots into. Synchronous so a test class can seed one in its
    /// constructor, which is where "every test here needs a team" belongs.
    protected Team SeedTeam(string clubName = "GJS", string teamName = "MO15-2")
    {
        // Reused rather than added, so a second team under the same club does not trip the unique name.
        var club = Db.Clubs.FirstOrDefault(c => c.Name == clubName) ?? Db.Clubs.Add(new Club { Name = clubName }).Entity;

        var team = new Team { Name = teamName, Club = club };
        club.Teams.Add(team);

        Db.SaveChanges();

        CurrentTeam.Id = team.Id;
        return team;
    }

    protected async Task<List<Player>> SeedPlayersAsync(int count)
    {
        var players = Enumerable.Range(1, count)
            .Select(i => new Player
            {
                FirstName = $"P{i}",
                ShirtNumber = i,
                PreferredPosition = PlayerPosition.CM
            })
            .ToList();

        Db.Players.AddRange(players);
        await Db.SaveChangesAsync();
        return players;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// A new context over the one open connection, which is what keeps the in-memory database alive between them.
    private sealed class TestDbContextFactory(SqliteConnection connection, StatsCacheInvalidator invalidator) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(new DateInSqlInterceptor(), invalidator).Options);
    }
}
