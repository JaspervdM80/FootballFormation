using System.Data.Common;
using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// <summary>
/// What logging a goal at the touchline adds over storing one: where in the match it happened —
/// the half being played and the reading on the clock — and the scoreline that has to follow from
/// the goals on file. The minute anyone sees is derived from that pair; these tests are about the
/// pair being written, and <see cref="MatchClockReportTests"/> about what it is read back as.
/// </summary>
public class MatchGoalServiceTests : LiveMatchTestBase
{
    [Fact]
    public async Task A_goal_is_stamped_with_the_half_being_played_and_the_clock_it_was_scored_on()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();
        var firstHalf = (await ReloadAsync(game.Id)).Periods.Single(p => p.PeriodType == PeriodType.FirstHalf);

        Time.Advance(TimeSpan.FromSeconds(1500));   // 25:00

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        Assert.Equal(firstHalf.Id, goal.Value!.GamePeriodId);
        Assert.Equal(1500, goal.Value.AtSeconds);

        // Nothing presentational on the row: the minute is the clock's to derive, and stays null
        // so that a half whose timings are corrected later takes its goals with it.
        Assert.Null(goal.Value.Minute);
    }

    /// <summary>
    /// The half is recorded, not inferred from the clock reading. The two disagree the moment a
    /// first half over-runs — this one is three minutes long past its 30 — and inferring is what
    /// let a change to the match duration silently reinterpret goals already on file.
    /// </summary>
    [Fact]
    public async Task A_second_half_goal_is_stamped_with_the_second_half_however_long_the_first_ran()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(33));      // a first half that ran three minutes long
        await MatchClock.EndHalfAsync(game.Id);
        await MatchClock.StartNextHalfAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(5));       // five minutes into the second half

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var reloaded = await ReloadAsync(game.Id);
        var secondHalf = reloaded.Periods.Single(p => p.PeriodType == PeriodType.SecondHalf);
        Assert.Equal(secondHalf.Id, goal.Value!.GamePeriodId);
        Assert.Equal(38 * 60, goal.Value.AtSeconds);

        // 35:xx on the scoreboard, so the 36th minute — not the 39th the raw elapsed time reads.
        Assert.Equal(new MatchMinute(36, 0), MatchClockReport.MinuteOf(reloaded, goal.Value));
    }

    /// <summary>
    /// A goal after the half has been played out belongs to that half, and is shown 30+2 rather
    /// than 32. The clock reading runs on past the cap; only the display stops at it.
    /// </summary>
    [Fact]
    public async Task A_goal_in_stoppage_time_belongs_to_the_half_that_is_over_running()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(31));      // one minute past a 30-minute half

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var reloaded = await ReloadAsync(game.Id);
        Assert.Equal(PeriodType.FirstHalf,
            reloaded.Periods.Single(p => p.Id == goal.Value!.GamePeriodId).PeriodType);
        Assert.Equal(31 * 60, goal.Value!.AtSeconds);
        Assert.Equal(new MatchMinute(30, 2), MatchClockReport.MinuteOf(reloaded, goal.Value));
    }

    [Fact]
    public async Task A_goal_for_us_needs_a_scorer()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var result = await Goals.LogGoalAsync(game.Id, null, null, false, false);

        Assert.True(result.IsFailure);
        Assert.Equal("A goal for us needs a scorer", result.Error);
    }

    [Fact]
    public async Task Removing_a_goal_pulls_the_scoreline_back_in_step()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.Equal(1, (await ReloadAsync(game.Id)).ScoreHome);

        await Goals.RemoveGoalAsync(game.Id, goal.Value!.Id);

        Assert.Equal(0, (await ReloadAsync(game.Id)).ScoreHome);
    }

    [Fact]
    public async Task Removing_a_goal_that_is_not_there_leaves_the_scoreline_alone()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var result = await Goals.RemoveGoalAsync(game.Id, 999);

        Assert.True(result.IsFailure);
        Assert.Equal(1, (await ReloadAsync(game.Id)).ScoreHome);
    }

    /// <summary>
    /// The goal row and the scoreline it produces reach the database together. They used to be a
    /// save each in a context each — and no transaction spans two contexts, so a lock timeout or a
    /// deploy restarting the container between them left the goal on file with a stale score beside
    /// it. One commit is the property that fixed it, and it is invisible from the outside until
    /// something interrupts the two halves, so it is counted here instead.
    /// </summary>
    [Fact]
    public async Task A_logged_goal_and_the_scoreline_it_makes_are_committed_together()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        var commits = new CommitCountingDbContextFactory(Db.Database.GetDbConnection());

        var goal = await GoalsOver(commits).LogGoalAsync(game.Id, players[1].Id, null, false, false);

        Assert.True(goal.IsSuccess);
        Assert.Equal(1, (await ReloadAsync(game.Id)).ScoreHome);
        Assert.Equal(1, commits.Count);
    }

    /// <inheritdoc cref="A_logged_goal_and_the_scoreline_it_makes_are_committed_together"/>
    [Fact]
    public async Task Removing_a_goal_and_the_scoreline_it_leaves_are_committed_together()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();
        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var commits = new CommitCountingDbContextFactory(Db.Database.GetDbConnection());

        var removed = await GoalsOver(commits).RemoveGoalAsync(game.Id, goal.Value!.Id);

        Assert.True(removed.IsSuccess);
        Assert.Equal(0, (await ReloadAsync(game.Id)).ScoreHome);
        Assert.Equal(1, commits.Count);
    }

    /// <summary>
    /// The scoreline counts the goal rows as the database has them, not as the caller last saw
    /// them. Counting in memory ahead of the insert would read the same way here and be a
    /// read-modify-write two touchline devices could both get wrong.
    /// </summary>
    [Fact]
    public async Task The_scoreline_counts_a_goal_logged_behind_this_ones_back()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // On file without the scoreline following it — a goal the next recount has to pick up.
        Db.GameGoals.Add(new GameGoal { GameId = game.Id, ScorerId = players[1].Id, Minute = 3 });
        await Db.SaveChangesAsync();

        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        Assert.Equal(2, (await ReloadAsync(game.Id)).ScoreHome);
    }

    /// <summary>The same service, wired to a factory that can be asked what it was made to write.</summary>
    private MatchGoalService GoalsOver(IDbContextFactory<AppDbContext> factory) =>
        new(factory,
            new GameService(factory, Seasons, CurrentUser, Time, NullLogger<GameService>.Instance),
            Notifier, Time, CurrentUser, NullLogger<MatchGoalService>.Instance);

    /// <summary>
    /// Hands out contexts over the test's one connection, like the base fixture does, and counts
    /// what is committed through them — EF raises this for the transaction it opens around a lone
    /// <c>SaveChanges</c> as well as for an explicit one, so two saves that commit once count once.
    /// </summary>
    private sealed class CommitCountingDbContextFactory(DbConnection connection)
        : IDbContextFactory<AppDbContext>
    {
        private readonly CommitCounter _counter = new();

        public int Count => _counter.Count;

        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new DateInSqlInterceptor(), _counter)
                .Options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        private sealed class CommitCounter : IDbTransactionInterceptor
        {
            private int _count;

            public int Count => _count;

            public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
                Interlocked.Increment(ref _count);

            public Task TransactionCommittedAsync(
                DbTransaction transaction, TransactionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _count);
                return Task.CompletedTask;
            }
        }
    }
}
