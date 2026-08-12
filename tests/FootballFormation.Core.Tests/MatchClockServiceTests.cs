using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The clock and period arithmetic — the numbers a season's statistics are later built from, and
/// the reason the match is driven to exact instants here rather than to a wall clock.
/// </summary>
public class MatchClockServiceTests : LiveMatchTestBase
{
    // ---- Starting and stopping -------------------------------------------------------------

    [Fact]
    public async Task Starting_a_match_puts_the_first_period_on_the_pitch_with_a_zeroed_clock()
    {
        var game = await SeedGameAsync();

        var result = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(result.IsSuccess);
        var started = await ReloadAsync(game.Id);
        Assert.Equal(MatchState.InProgress, started.MatchState);
        Assert.Equal(0, started.ClockAccumulatedSeconds);
        Assert.True(started.IsClockRunning);
        Assert.Equal(started.Periods.OrderBy(p => p.PeriodType).First().Id, started.LivePeriodId);
        Assert.Equal(0, started.Periods.OrderBy(p => p.PeriodType).First().StartedAtSeconds);
    }

    [Fact]
    public async Task A_match_cannot_be_started_twice()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var second = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(second.IsFailure);
        Assert.Equal("This match has already been started", second.Error);
    }

    [Fact]
    public async Task A_game_with_no_periods_cannot_kick_off()
    {
        var season = Season.CreateFor(KickOff);
        Db.Seasons.Add(season);
        await Db.SaveChangesAsync();

        var game = new Game { Opponent = "X", Date = KickOff.Date, SeasonId = season.Id };
        Db.Games.Add(game);
        await Db.SaveChangesAsync();

        var result = await MatchClock.StartMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This game has no periods to play", result.Error);
    }

    [Fact]
    public async Task An_unknown_game_fails_rather_than_throwing()
    {
        var result = await MatchClock.StartMatchAsync(999);

        Assert.True(result.IsFailure);
        Assert.Contains("999", result.Error);
    }

    // ---- The clock -------------------------------------------------------------------------

    [Fact]
    public async Task The_clock_runs_from_kick_off_until_the_period_is_whistled_off()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        // There is nothing that stops it in between: no pause, no resume. Whatever the touchline
        // does with the screen, the seconds the season's minutes are built from keep counting.
        Time.Advance(TimeSpan.FromMinutes(7));

        var running = await ReloadAsync(game.Id);
        Assert.True(running.IsClockRunning);
        Assert.Equal(420, running.ElapsedSecondsAt(Time.GetUtcNow().UtcDateTime));
    }

    // ---- Periods ---------------------------------------------------------------------------

    [Fact]
    public async Task Ending_a_period_stops_the_clock_and_leaves_nothing_live()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndPeriodAsync(game.Id);

        var ended = await ReloadAsync(game.Id);
        Assert.Null(ended.LivePeriodId);
        Assert.False(ended.IsClockRunning);
        Assert.Equal(1800, ended.Periods.OrderBy(p => p.PeriodType).First().EndedAtSeconds);
    }

    [Fact]
    public async Task The_second_half_starts_where_the_first_left_off_and_the_break_costs_nothing()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.EndPeriodAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));      // half time
        await MatchClock.StartNextPeriodAsync(game.Id);

        var second = await ReloadAsync(game.Id);
        var periods = second.Periods.OrderBy(p => p.PeriodType).ToList();

        Assert.Equal(1800, periods[1].StartedAtSeconds);
        Assert.Equal(periods[1].Id, second.LivePeriodId);
        Assert.True(second.IsClockRunning);
        // Half time added no match time.
        Assert.Equal(1800, second.ClockAccumulatedSeconds);
    }

    [Fact]
    public async Task The_next_period_cannot_start_before_the_current_one_ends()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var result = await MatchClock.StartNextPeriodAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("End the current period first", result.Error);
    }

    [Fact]
    public async Task Advancing_between_quarters_loses_no_seconds()
    {
        // Q1 → Q2 is not a real break: the clock must run straight through the changeover.
        var game = await SeedGameAsync(GameSplitType.Quarters);
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));
        await MatchClock.AdvancePeriodAsync(game.Id);

        var advanced = await ReloadAsync(game.Id);
        var periods = advanced.Periods.OrderBy(p => p.PeriodType).ToList();

        Assert.Equal(900, periods[0].EndedAtSeconds);
        Assert.Equal(900, periods[1].StartedAtSeconds);   // both ends read the same instant
        Assert.Equal(periods[1].Id, advanced.LivePeriodId);
        Assert.True(advanced.IsClockRunning);
    }

    [Fact]
    public async Task Advancing_restarts_a_clock_that_an_older_build_left_stopped()
    {
        // Nothing here can stop a live period's clock any more, but a row written while there was
        // a pause button can be in exactly that state. Rolling on to the next line-up while the
        // anchor stayed null would bank no minutes for the rest of the half.
        var game = await SeedGameAsync(GameSplitType.Quarters);
        await MatchClock.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));

        // Through ReloadAsync, which clears the change tracker first: the copy this context has
        // held since the seed still reads ClockRunningSince = null, so assigning null to *that*
        // is not a change EF would detect, and the anchor the service wrote would survive.
        var paused = await ReloadAsync(game.Id);
        paused.ClockAccumulatedSeconds = 900;
        paused.ClockRunningSince = null;
        await Db.SaveChangesAsync();

        Assert.True((await MatchClock.AdvancePeriodAsync(game.Id)).IsSuccess);

        var advanced = await ReloadAsync(game.Id);
        Assert.True(advanced.IsClockRunning);
        Assert.Equal(900, advanced.Periods.OrderBy(p => p.PeriodType).ToList()[1].StartedAtSeconds);

        Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1200, advanced.ElapsedSecondsAt(Time.GetUtcNow().UtcDateTime));
    }

    /// <summary>
    /// The plan for the next quarter was written before the match. If it still takes off a player
    /// who has already gone off, carrying it out pulls their replacement straight back off — so an
    /// injury replacement would last exactly one quarter. The live screen drops that swap from
    /// "Changes at half-way"; this is the half that makes the button agree with the card.
    /// </summary>
    [Fact]
    public async Task Advancing_keeps_a_player_brought_on_live_rather_than_carrying_out_the_swap_they_answered()
    {
        var game = await SeedQuartersWithASwapAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(5));
        // Not P3, who Q2 was going to bring on — an injury, and whoever was warm goes on.
        Assert.True((await Subs.SubstituteAsync(game.Id, players[1].Id, players[3].Id)).IsSuccess);

        Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True((await MatchClock.AdvancePeriodAsync(game.Id)).IsSuccess);

        var q2 = await LineupAsync(game.Id, PeriodType.SecondQuarter);
        var stayedOn = Assert.Single(q2, p => p.PlayerId == players[3].Id);
        Assert.False(stayedOn.IsSubstitute);
        Assert.Equal(5, stayedOn.SlotIndex);
        Assert.Equal(PlayerPosition.CM, stayedOn.Position);

        // And the arrival the plan named is on the bench rather than in the same slot.
        Assert.True(q2.Single(p => p.PlayerId == players[2].Id).IsSubstitute);
        Assert.Single(q2, p => p.SlotIndex == 5);
    }

    [Fact]
    public async Task Advancing_carries_out_a_swap_the_match_has_not_already_answered()
    {
        var game = await SeedQuartersWithASwapAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(15));
        Assert.True((await MatchClock.AdvancePeriodAsync(game.Id)).IsSuccess);

        // Nothing overtook it, so the planned line-up rolls on untouched.
        var q2 = await LineupAsync(game.Id, PeriodType.SecondQuarter);
        Assert.Equal(5, q2.Single(p => p.PlayerId == players[2].Id).SlotIndex);
        Assert.True(q2.Single(p => p.PlayerId == players[1].Id).IsSubstitute);
    }

    /// <summary>
    /// A quarters game whose second quarter plans one swap: P2 comes off at CM for P3. Every
    /// period is seeded with the same line-up, so the second one is rewritten here.
    /// </summary>
    private async Task<Game> SeedQuartersWithASwapAsync()
    {
        var game = await SeedGameAsync(GameSplitType.Quarters);
        var players = await PlayersAsync();

        var q2 = game.Periods.Single(p => p.PeriodType == PeriodType.SecondQuarter);
        await Db.Entry(q2).Collection(p => p.PlayerPositions).LoadAsync();

        var comingOff = q2.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        var comingOn = q2.PlayerPositions.Single(p => p.PlayerId == players[2].Id);

        (comingOff.SlotIndex, comingOff.IsSubstitute) = (null, true);
        (comingOn.SlotIndex, comingOn.IsSubstitute) = (5, false);
        comingOn.Position = PlayerPosition.CM;

        await Db.SaveChangesAsync();
        return game;
    }

    private async Task<List<GamePlayerPosition>> LineupAsync(int gameId, PeriodType period)
    {
        Db.ChangeTracker.Clear();

        return await Db.GamePlayerPositions
            .Where(p => p.GamePeriod.GameId == gameId && p.GamePeriod.PeriodType == period)
            .ToListAsync();
    }

    [Fact]
    public async Task Advancing_past_the_last_period_is_refused()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await MatchClock.AdvancePeriodAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));

        var result = await MatchClock.AdvancePeriodAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Every period has been played — finish the match instead", result.Error);
    }

    // ---- Finishing -------------------------------------------------------------------------

    [Fact]
    public async Task Finishing_closes_the_running_period_and_writes_the_score_from_the_goals()
    {
        var game = await SeedGameAsync();
        await MatchClock.StartMatchAsync(game.Id);

        var players = await PlayersAsync();
        Time.Advance(TimeSpan.FromMinutes(10));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, null, null, false, true);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, true, false);   // own goal

        await MatchClock.FinishMatchAsync(game.Id);

        var finished = await ReloadAsync(game.Id);
        Assert.Equal(MatchState.Finished, finished.MatchState);
        Assert.Null(finished.LivePeriodId);
        Assert.False(finished.IsClockRunning);
        Assert.Equal(1, finished.ScoreHome);
        Assert.Equal(2, finished.ScoreAway);   // their goal plus our own goal
        Assert.Equal(1200, finished.Periods.OrderBy(p => p.PeriodType).First().EndedAtSeconds);
    }

    [Fact]
    public async Task A_match_that_never_started_cannot_be_finished()
    {
        var game = await SeedGameAsync();

        var result = await MatchClock.FinishMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This match has not been started", result.Error);
    }
}
