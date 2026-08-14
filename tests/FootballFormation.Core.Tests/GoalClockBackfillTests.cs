using FootballFormation.Core.Data;
using FootballFormation.Core.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The one migration in here that rewrites rows rather than adding columns to them.
/// <para>
/// <c>StoreGoalPeriodAndClock</c> moves goals logged in stoppage time onto the clock, and it runs
/// unattended against the live volume on the next deploy — so what it does is checked here rather
/// than only rehearsed by hand. Every other migration is exercised implicitly by
/// <c>ServiceTestBase</c>, which builds the schema from the model; this one has a body worth
/// asserting on, and the property that matters is not a column value but what the app then shows:
/// a goal written <c>30+2</c> still reads <c>30+2</c>.
/// </para>
/// <para>
/// Migrated rather than created, because a backfill only exists on the path from the old schema.
/// Two contexts over one held-open in-memory connection, the same arrangement
/// <c>ServiceTestBase</c> uses.
/// </para>
/// </summary>
public class GoalClockBackfillTests : IDisposable
{
    private const string BeforeTheBackfill = "20260813062317_AddGoalAdditionalMinute";

    /// <summary>Ids given to the two halves below, so the assertions can name them.</summary>
    private const int FirstHalfId = 10;
    private const int SecondHalfId = 11;

    private readonly SqliteConnection _connection;

    public GoalClockBackfillTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
    }

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new DateInSqlInterceptor())
            .Options);

    /// <summary>
    /// A 60-minute match in halves whose first half over-ran to 33:00, so the second half kicked
    /// off there and not at the 30:00 its scoreboard restarts at. That gap is the whole difficulty:
    /// a reconstruction that ignored the real kick-off would put every second-half goal three
    /// minutes out.
    /// </summary>
    private async Task SeedBeforeTheBackfillAsync()
    {
        await using (var db = Context())
            await db.GetService<IMigrator>().MigrateAsync(BeforeTheBackfill);

        var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO Seasons (Name, StartDate, EndDate, IsCurrent)
              VALUES ('25/26','2026-08-01','2027-06-30',1);
            INSERT INTO Games (SeasonId, Opponent, Date, ScoreHome, ScoreAway, FormationType,
                               SplitType, GameDurationMinutes, IsHomeGame, MatchState,
                               ClockAccumulatedSeconds, MatchType, GuestPlayerIds, UnavailablePlayerIds)
              VALUES (1,'Opp','2026-08-10',0,4,0,0,60,1,2,4200,0,'','');
            INSERT INTO GamePeriods (Id, GameId, PeriodType, StartedAtSeconds, EndedAtSeconds)
              VALUES ({FirstHalfId},1,0,0,1980), ({SecondHalfId},1,1,1980,4200);

            -- Written by the live screen as a minute and an overrun, the shape being replaced.
            INSERT INTO GameGoals (Id, GameId, Minute, AdditionalMinute, IsOwnGoal, IsOpponentGoal, RecordedAt)
              VALUES (1,1,30,2,0,1,'2026-08-10 10:32:00'),     -- first-half stoppage
                     (2,1,60,1,0,1,'2026-08-10 11:05:00'),     -- second-half stoppage
                     (3,1,37, 0,0,1,'2026-08-10 10:40:00'),    -- a plain minute: ambiguous
                     (4,1,NULL,0,0,1,'2026-08-10 10:41:00');   -- no minute at all
            """;
        await cmd.ExecuteNonQueryAsync();

        await using (var db = Context()) await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task A_goal_scored_in_stoppage_time_still_reads_as_stoppage_time_afterwards()
    {
        await SeedBeforeTheBackfillAsync();

        await using var db = Context();
        var game = await db.Games.Include(g => g.Periods).Include(g => g.Goals).FirstAsync();

        string Shown(int goalId) =>
            MatchClockReport.MinuteOf(game, game.Goals.Single(g => g.Id == goalId))?.ToString() ?? "—";

        // 32 would be the 32nd minute — two minutes into the second half — which is not when
        // either of these was scored.
        Assert.Equal("30+2", Shown(1));
        Assert.Equal("60+1", Shown(2));

        Assert.Equal("37", Shown(3));
        Assert.Equal("—", Shown(4));
    }

    [Fact]
    public async Task The_overrun_is_rewritten_as_the_half_and_the_clock_it_was_scored_on()
    {
        await SeedBeforeTheBackfillAsync();

        await using var db = Context();
        var goals = await db.GameGoals.ToDictionaryAsync(g => g.Id);

        // 30:00 into a first half that started at 0, plus the minute of stoppage already counted.
        Assert.Equal(FirstHalfId, goals[1].GamePeriodId);
        Assert.Equal(31 * 60, goals[1].AtSeconds);

        // The second half really kicked off at 33:00, so its 60+1 is 63 minutes of play — not the
        // 61 the scoreboard reading alone would have suggested.
        Assert.Equal(SecondHalfId, goals[2].GamePeriodId);
        Assert.Equal(63 * 60, goals[2].AtSeconds);
    }

    /// <summary>
    /// A goal without an overrun says nothing about which half it belongs to — a stored 37 on a
    /// 30-minute half could be a minute somebody typed on the result page. Guessing one is how the
    /// frozen minute went wrong in the first place, so these rows are left exactly as they are.
    /// </summary>
    [Fact]
    public async Task A_goal_with_only_a_minute_is_left_alone_rather_than_guessed_at()
    {
        await SeedBeforeTheBackfillAsync();

        await using var db = Context();
        var goals = await db.GameGoals.ToDictionaryAsync(g => g.Id);

        Assert.Null(goals[3].GamePeriodId);
        Assert.Null(goals[3].AtSeconds);
        Assert.Equal(37, goals[3].Minute);

        Assert.Null(goals[4].GamePeriodId);
        Assert.Null(goals[4].AtSeconds);
        Assert.Null(goals[4].Minute);
    }

    /// <summary>
    /// The reason the backfill is worth doing at all: on the elapsed clock the rewritten goals sit
    /// inside the halves they were scored in, so the timeline and the running score agree with the
    /// minutes on screen.
    /// </summary>
    [Fact]
    public async Task The_rewritten_goals_sort_inside_the_halves_they_were_scored_in()
    {
        await SeedBeforeTheBackfillAsync();

        await using var db = Context();
        var game = await db.Games.Include(g => g.Periods).Include(g => g.Goals).FirstAsync();

        var order = game.Goals
            .OrderBy(g => MatchClockReport.ElapsedOf(game, g))
            .ThenBy(g => g.RecordedAt)
            .Select(g => g.Id);

        // No minute, then 30+2, then the 37th minute, then 60+1.
        Assert.Equal([4, 1, 3, 2], order);
    }

    [Fact]
    public async Task The_migration_leaves_the_foreign_keys_intact()
    {
        await SeedBeforeTheBackfillAsync();

        var check = _connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check";
        await using var violations = await check.ExecuteReaderAsync();

        Assert.False(await violations.ReadAsync(), "the rebuilt GameGoals table has a broken row");
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
