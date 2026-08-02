using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
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

        Players = new PlayerService(DbFactory, NullLogger<PlayerService>.Instance);
        Seasons = new SeasonService(DbFactory, Time, NullLogger<SeasonService>.Instance);
        Squads = new SeasonSquadService(DbFactory, NullLogger<SeasonSquadService>.Instance);
        Games = new GameService(DbFactory, Seasons, NullLogger<GameService>.Instance);
        Preferences = new MatchPreferencesService(DbFactory, Time, NullLogger<MatchPreferencesService>.Instance);
        Live = new LiveMatchService(DbFactory, Games, new LiveMatchNotifier(), Time,
            NullLogger<LiveMatchService>.Instance);
    }

    /// <summary>A context for arranging and asserting. The services use their own, as in production.</summary>
    protected AppDbContext Db { get; }

    protected IDbContextFactory<AppDbContext> DbFactory { get; }
    protected FakeTimeProvider Time { get; }

    protected PlayerService Players { get; }
    protected SeasonService Seasons { get; }
    protected SeasonSquadService Squads { get; }
    protected GameService Games { get; }
    protected MatchPreferencesService Preferences { get; }
    protected LiveMatchService Live { get; }

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
    /// </summary>
    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }
}
