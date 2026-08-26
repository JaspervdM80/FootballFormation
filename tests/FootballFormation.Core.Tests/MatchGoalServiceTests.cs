using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// These are about the half-and-clock pair being written; <see cref="MatchClockReportTests"/> covers what it is read back as.
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

        // The minute stays null so a half whose timings are corrected later takes its goals with it.
        Assert.Null(goal.Value.Minute);
    }

    /// The half is recorded, not inferred: the two disagree the moment a first half over-runs, and inferring is what let a change to the
    /// match duration silently reinterpret goals already on file.
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

    /// The clock reading runs on past the cap; only the display stops at it, so a goal there is 30+2 rather than 32.
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

    /// <summary>One commit, because no transaction spans two contexts: a deploy restarting the container between two saves left the goal
    /// on file with a stale score beside it. Invisible from outside until something interrupts the pair, so it is counted here.</summary>
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

    /// Counts the rows as the database has them: counting in memory ahead of the insert reads the same here, but is a read-modify-write
    /// two touchline devices could both get wrong.
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

    /// The same service, wired to a factory that can be asked what it was made to write.
    private MatchGoalService GoalsOver(IDbContextFactory<AppDbContext> factory) =>
        new(factory,
            new GameService(factory, Seasons, CurrentUser, Time, NullLogger<GameService>.Instance),
            Notifier, Time, CurrentUser, NullLogger<MatchGoalService>.Instance);

    /// EF raises this for the transaction it opens around a lone SaveChanges as well as for an explicit one, so two saves that commit
    /// once count once.
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
