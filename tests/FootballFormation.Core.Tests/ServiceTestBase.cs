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

        // Before the services: the write guard asks the one about the other, as it does in the app.
        CurrentTeam = new FakeCurrentTeam();
        CurrentUser = new FakeCurrentUser(CurrentTeam);

        StatsCache = new StatsCache(new MemoryCache(new MemoryCacheOptions()));

        var factory = new TestDbContextFactory(_connection, new StatsCacheInvalidator(StatsCache), CurrentTeam);
        DbFactory = factory;
        RawDbFactory = factory;
        Db = DbFactory.CreateDbContext();
        Db.Database.EnsureCreated();

        Time = new FakeTimeProvider(Now);

        Players = new PlayerService(DbFactory, CurrentUser, NullLogger<PlayerService>.Instance);
        Seasons = new SeasonService(DbFactory, factory, Time, CurrentUser, NullLogger<SeasonService>.Instance);
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

        Stats = new StatsService(Games, Squads, Trainings, CurrentTeam, Time, StatsCache, NullLogger<StatsService>.Instance);
    }

    /// An admin by default, so a test about something else does not have to say so.
    protected FakeCurrentUser CurrentUser { get; }

    /// The team in scope. <see cref="SeedTeam"/> points it at what it seeded; a test about something else leaves it unset, which the
    /// fake reads as "every team".
    protected FakeCurrentTeam CurrentTeam { get; }

    /// For arranging and asserting. The services use their own, as in production.
    protected AppDbContext Db { get; }

    protected IDbContextFactory<AppDbContext> DbFactory { get; }

    /// The unstamped factory CurrentTeam and the season boot loops take — see TeamScopedDbContextFactory.
    protected IRawDbContextFactory RawDbFactory { get; }

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
        var team = EnsureScopedTeam();

        var season = Season.CreateFor(covering ?? Now);
        season.TeamId = team;
        season.IsCurrent = isCurrent;

        Db.Seasons.Add(season);
        await Db.SaveChangesAsync();
        return season;
    }

    /// A club with one team, put in scope — the shape every deployment boots into. Synchronous so a test class can seed one in its
    /// constructor, which is where "every test here needs a team" belongs.
    protected Team SeedTeam(string clubName = "GJS", string teamName = "MO15-2")
    {
        // Idempotent by name: a test that names this team after a helper already seeded the default reuses it rather than tripping the
        // unique (club, name). A second, differently named team is a genuinely new one.
        var existing = Db.Teams.Include(t => t.Club).FirstOrDefault(t => t.Club!.Name == clubName && t.Name == teamName);
        if (existing is not null)
        {
            CurrentTeam.Id = existing.Id;
            CurrentTeam.ClubId = existing.ClubId;
            return existing;
        }

        var club = Db.Clubs.FirstOrDefault(c => c.Name == clubName) ?? Db.Clubs.Add(new Club { Name = clubName }).Entity;

        var team = new Team { Name = teamName, Club = club };
        club.Teams.Add(team);

        Db.SaveChanges();

        CurrentTeam.Id = team.Id;
        CurrentTeam.ClubId = club.Id;
        return team;
    }

    /// The team the seed helpers hang their data off. A test that never named one still needs a real team now that the data carries it,
    /// so seed the default and put it in scope; a test that already did keeps its own.
    protected int EnsureScopedTeam()
    {
        if (CurrentTeam.Id is null) SeedTeam();
        return CurrentTeam.Id!.Value;
    }

    protected async Task<List<Player>> SeedPlayersAsync(int count)
    {
        EnsureScopedTeam();

        var players = Enumerable.Range(1, count)
            .Select(i => new Player
            {
                FirstName = $"P{i}",
                ClubId = CurrentTeam.ClubId!.Value,
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

    /// A new context over the one open connection, which is what keeps the in-memory database alive between them. Serves the team-scoped
    /// contexts everything takes and the raw ones SeasonService's boot loops stamp by hand — the two roles the app splits between the
    /// scoped factory and the raw one.
    private sealed class TestDbContextFactory(
        SqliteConnection connection, StatsCacheInvalidator invalidator, FakeCurrentTeam currentTeam)
        : IDbContextFactory<AppDbContext>, IRawDbContextFactory
    {
        private DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new DateInSqlInterceptor(), invalidator)
                .Options;

        AppDbContext IDbContextFactory<AppDbContext>.CreateDbContext() => new ScopedTestDbContext(Options(), currentTeam);

        AppDbContext IRawDbContextFactory.CreateDbContext() => new(Options());
    }

    /// Tracks the fake team in scope live, so switching CurrentTeam mid-test scopes every context at once and the long-lived arranging
    /// context sees the same team the services do — the query filters read these two members per query.
    private sealed class ScopedTestDbContext(DbContextOptions<AppDbContext> options, FakeCurrentTeam currentTeam) : AppDbContext(options)
    {
        public override int? CurrentTeamId
        {
            get => currentTeam.Id;
            set { }
        }

        public override int? CurrentClubId
        {
            get => currentTeam.ClubId;
            set { }
        }
    }
}
