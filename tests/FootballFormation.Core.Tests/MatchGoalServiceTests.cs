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
}
