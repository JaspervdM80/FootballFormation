using System.Data.Common;
using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// <summary>
/// What logging a goal at the touchline adds over storing one: the minute it is stamped with, and
/// the scoreline that has to follow from the goals on file.
/// </summary>
public class MatchGoalServiceTests : LiveMatchTestBase
{
    [Fact]
    public async Task A_goal_is_stamped_with_the_minute_the_clock_showed_counting_from_one()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        // Minute 0 reads oddly on a timeline, so the first minute of play is 1'.
        var opening = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.Equal(1, opening.Value!.Minute);

        Time.Advance(TimeSpan.FromSeconds(1500));   // 25:00
        var later = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.Equal(26, later.Value!.Minute);
    }

    /// <summary>
    /// The second half's clock starts at half the match however long the first half really took,
    /// and the goal minute follows the clock — otherwise every second-half goal is pushed out by
    /// the first half's overrun.
    /// </summary>
    [Fact]
    public async Task A_second_half_goal_is_stamped_off_the_scoreboard_clock_not_the_elapsed_time()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        var players = await PlayersAsync();

        Time.Advance(TimeSpan.FromMinutes(33));      // a first half that ran three minutes long
        await MatchClock.EndPeriodAsync(game.Id);
        await MatchClock.StartNextPeriodAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(5));       // five minutes into the second half

        var goal = await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        // 35:xx on the clock, so the 36th minute — not the 39th the raw elapsed time would give.
        Assert.Equal(36, goal.Value!.Minute);
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
