using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The clock, period and substitution arithmetic, driven against a real SQLite database so the
/// value converters, cascade rules and unique indexes are exercised too — an in-memory provider
/// would accept schemas SQLite rejects.
/// </summary>
public class LiveMatchServiceTests : ServiceTestBase
{
    private static readonly DateTime KickOff = Now;

    /// <summary>A 60-minute game in halves with both periods laid out and a lineup on the pitch.</summary>
    private async Task<Game> SeedGameAsync(GameSplitType split = GameSplitType.Halves)
    {
        var season = Season.CreateFor(KickOff);
        Db.Seasons.Add(season);

        var players = Enumerable.Range(1, 4)
            .Select(i => new Player { FirstName = $"P{i}", ShirtNumber = i, PreferredPosition = PlayerPosition.CM })
            .ToList();
        Db.Players.AddRange(players);
        await Db.SaveChangesAsync();

        var game = new Game
        {
            Opponent = "Opponent",
            Date = KickOff.Date,
            SeasonId = season.Id,
            SplitType = split,
            GameDurationMinutes = 60
        };

        foreach (var type in PeriodTypeExtensions.ForSplitType(split))
        {
            game.Periods.Add(new GamePeriod
            {
                PeriodType = type,
                PlayerPositions =
                [
                    new GamePlayerPosition { PlayerId = players[0].Id, Position = PlayerPosition.GK, SlotIndex = 0 },
                    new GamePlayerPosition { PlayerId = players[1].Id, Position = PlayerPosition.CM, SlotIndex = 5 },
                    new GamePlayerPosition { PlayerId = players[2].Id, Position = PlayerPosition.CM, IsSubstitute = true }
                ]
            });
        }

        Db.Games.Add(game);
        await Db.SaveChangesAsync();
        return game;
    }

    private async Task<Game> ReloadAsync(int gameId)
    {
        Db.ChangeTracker.Clear();
        return await Db.Games.Include(g => g.Periods).FirstAsync(g => g.Id == gameId);
    }

    // ---- Starting and stopping -------------------------------------------------------------

    [Fact]
    public async Task Starting_a_match_puts_the_first_period_on_the_pitch_with_a_zeroed_clock()
    {
        var game = await SeedGameAsync();

        var result = await Live.StartMatchAsync(game.Id);

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
        await Live.StartMatchAsync(game.Id);

        var second = await Live.StartMatchAsync(game.Id);

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

        var result = await Live.StartMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This game has no periods to play", result.Error);
    }

    [Fact]
    public async Task An_unknown_game_fails_rather_than_throwing()
    {
        var result = await Live.StartMatchAsync(999);

        Assert.True(result.IsFailure);
        Assert.Contains("999", result.Error);
    }

    // ---- The clock -------------------------------------------------------------------------

    [Fact]
    public async Task Pausing_banks_the_time_run_so_far_and_stops_the_anchor()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(7));
        await Live.PauseClockAsync(game.Id);

        var paused = await ReloadAsync(game.Id);
        Assert.Equal(420, paused.ClockAccumulatedSeconds);
        Assert.False(paused.IsClockRunning);
    }

    [Fact]
    public async Task Time_spent_paused_is_not_counted()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(7));
        await Live.PauseClockAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));      // a long injury stoppage
        await Live.ResumeClockAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(3));

        var resumed = await ReloadAsync(game.Id);
        Assert.Equal(600, resumed.ElapsedSecondsAt(Time.GetUtcNow().UtcDateTime));
    }

    [Fact]
    public async Task Pausing_a_stopped_clock_is_refused()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        await Live.PauseClockAsync(game.Id);

        var result = await Live.PauseClockAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("The clock is not running", result.Error);
    }

    [Fact]
    public async Task Resuming_a_running_clock_is_refused()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        var result = await Live.ResumeClockAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("The clock is already running", result.Error);
    }

    // ---- Periods ---------------------------------------------------------------------------

    [Fact]
    public async Task Ending_a_period_stops_the_clock_and_leaves_nothing_live()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await Live.EndPeriodAsync(game.Id);

        var ended = await ReloadAsync(game.Id);
        Assert.Null(ended.LivePeriodId);
        Assert.False(ended.IsClockRunning);
        Assert.Equal(1800, ended.Periods.OrderBy(p => p.PeriodType).First().EndedAtSeconds);
    }

    [Fact]
    public async Task The_second_half_starts_where_the_first_left_off_and_the_break_costs_nothing()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(30));
        await Live.EndPeriodAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));      // half time
        await Live.StartNextPeriodAsync(game.Id);

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
        await Live.StartMatchAsync(game.Id);

        var result = await Live.StartNextPeriodAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("End the current period first", result.Error);
    }

    [Fact]
    public async Task Advancing_between_quarters_loses_no_seconds()
    {
        // Q1 → Q2 is not a real break: the clock must run straight through the changeover.
        var game = await SeedGameAsync(GameSplitType.Quarters);
        await Live.StartMatchAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(15));
        await Live.AdvancePeriodAsync(game.Id);

        var advanced = await ReloadAsync(game.Id);
        var periods = advanced.Periods.OrderBy(p => p.PeriodType).ToList();

        Assert.Equal(900, periods[0].EndedAtSeconds);
        Assert.Equal(900, periods[1].StartedAtSeconds);   // both ends read the same instant
        Assert.Equal(periods[1].Id, advanced.LivePeriodId);
        Assert.True(advanced.IsClockRunning);
    }

    [Fact]
    public async Task Advancing_past_the_last_period_is_refused()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));
        await Live.AdvancePeriodAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(30));

        var result = await Live.AdvancePeriodAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Every period has been played — finish the match instead", result.Error);
    }

    // ---- Finishing -------------------------------------------------------------------------

    [Fact]
    public async Task Finishing_closes_the_running_period_and_writes_the_score_from_the_goals()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();
        Time.Advance(TimeSpan.FromMinutes(10));
        await Live.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Live.LogGoalAsync(game.Id, null, null, false, true);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Live.LogGoalAsync(game.Id, players[1].Id, null, true, false);   // own goal

        await Live.FinishMatchAsync(game.Id);

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

        var result = await Live.FinishMatchAsync(game.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("This match has not been started", result.Error);
    }

    // ---- Goals -----------------------------------------------------------------------------

    [Fact]
    public async Task A_goal_is_stamped_with_the_minute_the_clock_showed_counting_from_one()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        // Minute 0 reads oddly on a timeline, so the first minute of play is 1'.
        var opening = await Live.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.Equal(1, opening.Value!.Minute);

        Time.Advance(TimeSpan.FromSeconds(1500));   // 25:00
        var later = await Live.LogGoalAsync(game.Id, players[1].Id, null, false, false);
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
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        Time.Advance(TimeSpan.FromMinutes(33));      // a first half that ran three minutes long
        await Live.EndPeriodAsync(game.Id);
        await Live.StartNextPeriodAsync(game.Id);

        Time.Advance(TimeSpan.FromMinutes(5));       // five minutes into the second half

        var goal = await Live.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        // 35:xx on the clock, so the 36th minute — not the 39th the raw elapsed time would give.
        Assert.Equal(36, goal.Value!.Minute);
    }

    [Fact]
    public async Task A_goal_for_us_needs_a_scorer()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);

        var result = await Live.LogGoalAsync(game.Id, null, null, false, false);

        Assert.True(result.IsFailure);
        Assert.Equal("A goal for us needs a scorer", result.Error);
    }

    [Fact]
    public async Task Removing_a_goal_pulls_the_scoreline_back_in_step()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        var goal = await Live.LogGoalAsync(game.Id, players[1].Id, null, false, false);
        Assert.Equal(1, (await ReloadAsync(game.Id)).ScoreHome);

        await Live.RemoveGoalAsync(game.Id, goal.Value!.Id);

        Assert.Equal(0, (await ReloadAsync(game.Id)).ScoreHome);
    }

    // ---- Substitutions ---------------------------------------------------------------------

    [Fact]
    public async Task A_substitution_hands_the_slot_and_position_over()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var result = await Live.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(720, result.Value!.AtSeconds);
        Assert.Equal(13, result.Value.Minute);
        Assert.Equal(PlayerPosition.CM, result.Value.Position);
        Assert.Equal(5, result.Value.SlotIndex);

        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var off = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        var on = period.PlayerPositions.Single(p => p.PlayerId == players[2].Id);

        Assert.True(off.IsSubstitute);
        Assert.Null(off.SlotIndex);
        Assert.False(on.IsSubstitute);
        Assert.Equal(5, on.SlotIndex);
        Assert.Equal(PlayerPosition.CM, on.Position);
    }

    [Fact]
    public async Task A_player_cannot_be_substituted_for_themselves()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        var result = await Live.SubstituteAsync(game.Id, players[1].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("A player cannot be substituted for themselves", result.Error);
    }

    [Fact]
    public async Task Only_a_player_on_the_pitch_can_come_off()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        // players[2] is on the bench.
        var result = await Live.SubstituteAsync(game.Id, players[2].Id, players[1].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("That player is not on the pitch", result.Error);
    }

    [Fact]
    public async Task Someone_who_turned_up_late_can_still_be_brought_on()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        // players[3] is in no lineup at all — refusing the change mid-match is less useful
        // than adding them.
        var result = await Live.SubstituteAsync(game.Id, players[1].Id, players[3].Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_substitution_needs_a_period_to_be_running()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        await Live.EndPeriodAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        var result = await Live.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Assert.True(result.IsFailure);
        Assert.Equal("No period is currently being played", result.Error);
    }

    [Fact]
    public async Task Undoing_the_most_recent_substitution_restores_the_slot()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        Time.Advance(TimeSpan.FromMinutes(12));
        var sub = await Live.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        var undone = await Live.RemoveSubstitutionAsync(sub.Value!.Id);
        Assert.True(undone.IsSuccess);

        Db.ChangeTracker.Clear();
        var live = await ReloadAsync(game.Id);
        var period = await Db.GamePeriods
            .Include(p => p.PlayerPositions)
            .FirstAsync(p => p.Id == live.LivePeriodId);

        var back = period.PlayerPositions.Single(p => p.PlayerId == players[1].Id);
        var benched = period.PlayerPositions.Single(p => p.PlayerId == players[2].Id);

        Assert.False(back.IsSubstitute);
        Assert.Equal(5, back.SlotIndex);
        Assert.True(benched.IsSubstitute);
        Assert.Null(benched.SlotIndex);
        Assert.Empty(await Db.GameSubstitutions.ToListAsync());
    }

    [Fact]
    public async Task Only_the_most_recent_substitution_of_a_period_can_be_undone()
    {
        var game = await SeedGameAsync();
        await Live.StartMatchAsync(game.Id);
        var players = await Db.Players.OrderBy(p => p.Id).ToListAsync();

        Time.Advance(TimeSpan.FromMinutes(10));
        var first = await Live.SubstituteAsync(game.Id, players[1].Id, players[2].Id);

        Time.Advance(TimeSpan.FromMinutes(10));
        await Live.SubstituteAsync(game.Id, players[2].Id, players[3].Id);

        // Reversing the earlier swap would fight every change made on that slot since.
        var result = await Live.RemoveSubstitutionAsync(first.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Only the most recent substitution of a period can be undone", result.Error);
    }

    // ---- Match day -------------------------------------------------------------------------

    [Fact]
    public async Task A_match_in_progress_wins_over_whatever_the_calendar_says()
    {
        var yesterdaysGame = await SeedGameAsync();
        yesterdaysGame.Date = KickOff.Date.AddDays(-1);
        await Db.SaveChangesAsync();
        await Live.StartMatchAsync(yesterdaysGame.Id);

        // It could have kicked off before midnight, and it is the one someone at a pitch is watching.
        var result = await Live.GetTodaysMatchAsync();

        Assert.Equal(yesterdaysGame.Id, result.Value!.Id);
    }

    [Fact]
    public async Task An_ordinary_day_has_no_match()
    {
        var game = await SeedGameAsync();
        game.Date = KickOff.Date.AddDays(7);
        await Db.SaveChangesAsync();

        var result = await Live.GetTodaysMatchAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
