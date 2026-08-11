using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Security;
using FootballFormation.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FootballFormation.Core.Tests;

/// <summary>
/// A real SQLite database per test, held open in memory for the life of the connection.
/// <para>
/// Not the in-memory provider: these services lean on foreign keys, unique indexes, cascade
/// behaviour and the CSV value converters, none of which the in-memory provider enforces. A test
/// that passes there can still fail against the database the app actually ships with.
/// </para>
/// <para>
/// Services are constructed here rather than in each test class, so the wiring lives in one place.
/// </para>
/// </summary>
public abstract class ServiceTestBase : IDisposable
{
    protected static readonly DateTime Now = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;

    protected ServiceTestBase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        DbFactory = new TestDbContextFactory(_connection);
        Db = DbFactory.CreateDbContext();
        Db.Database.EnsureCreated();

        Time = new FakeTimeProvider(Now);

        Players = new PlayerService(DbFactory, CurrentUser, NullLogger<PlayerService>.Instance);
        Seasons = new SeasonService(DbFactory, Time, CurrentUser, NullLogger<SeasonService>.Instance);
        Squads = new SeasonSquadService(DbFactory, CurrentUser, NullLogger<SeasonSquadService>.Instance);
        Games = new GameService(DbFactory, Seasons, CurrentUser, Time, NullLogger<GameService>.Instance);
        Preferences = new MatchPreferencesService(DbFactory, Time, CurrentUser,
            NullLogger<MatchPreferencesService>.Instance);
        Live = new LiveMatchService(DbFactory, Time, NullLogger<LiveMatchService>.Instance);

        // One notifier across the three, as in the app: they are separate services, but a
        // spectator's screen does not care which of them changed the match.
        MatchClock = new MatchClockService(DbFactory, Notifier, Time, CurrentUser,
            NullLogger<MatchClockService>.Instance);
        Goals = new MatchGoalService(DbFactory, Games, Notifier, Time, CurrentUser,
            NullLogger<MatchGoalService>.Instance);
        Subs = new MatchSubstitutionService(DbFactory, Notifier, Time, CurrentUser,
            NullLogger<MatchSubstitutionService>.Instance);

        Users = new UserService(DbFactory, CurrentUser, NullLogger<UserService>.Instance);
    }

    /// <summary>
    /// An admin by default, so a test that is about something else does not have to say so. Set
    /// <see cref="FakeCurrentUser.IsAdmin"/> to false to exercise the refusal path.
    /// </summary>
    protected FakeCurrentUser CurrentUser { get; } = new();

    /// <summary>A context for arranging and asserting. The services use their own, as in production.</summary>
    protected AppDbContext Db { get; }

    protected IDbContextFactory<AppDbContext> DbFactory { get; }
    protected FakeTimeProvider Time { get; }

    protected PlayerService Players { get; }
    protected SeasonService Seasons { get; }
    protected SeasonSquadService Squads { get; }
    protected GameService Games { get; }
    protected MatchPreferencesService Preferences { get; }

    /// <summary>
    /// The one every live write announces itself on, shared by the three services below the way the
    /// singleton is in the app. Subscribe to it to see what a spectator's screen would be told.
    /// </summary>
    protected LiveMatchNotifier Notifier { get; } = new();

    /// <summary>Reading a live match. The three below are how one is written to.</summary>
    protected LiveMatchService Live { get; }

    /// <summary>The match clock, not the <see cref="TimeProvider"/> driving it — that is <see cref="Time"/>.</summary>
    protected MatchClockService MatchClock { get; }

    protected MatchGoalService Goals { get; }
    protected MatchSubstitutionService Subs { get; }
    protected UserService Users { get; }

    /// <summary>A fresh context, for reading back what a service wrote without tracking interference.</summary>
    protected AppDbContext Read() => DbFactory.CreateDbContext();

    protected async Task<Season> SeedSeasonAsync(DateTime? covering = null, bool isCurrent = true)
    {
        var season = Season.CreateFor(covering ?? Now);
        season.IsCurrent = isCurrent;

        Db.Seasons.Add(season);
        await Db.SaveChangesAsync();
        return season;
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

    /// <summary>
    /// Hands every caller a new context over the one open connection, which is what keeps the
    /// in-memory database alive between them.
    /// <para>
    /// <see cref="DateInSqlInterceptor"/> rides along on every one of them, so the whole suite —
    /// not a single test that has to remember to look — is what stops a date comparison reaching
    /// SQL.
    /// </para>
    /// </summary>
    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new DateInSqlInterceptor())
                .Options);
    }
}
