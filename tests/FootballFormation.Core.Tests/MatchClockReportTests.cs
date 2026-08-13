using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The scoreboard clock. It is deliberately not the elapsed time: a half stops at the length of a
/// half, and the second half starts at half the match duration however long the first one really
/// took — otherwise an over-running first half drags the whole second half out of step.
/// </summary>
public class MatchClockReportTests
{
    /// <summary>A 60-minute game in quarters: two 30-minute halves, Q1/Q2 then Q3/Q4.</summary>
    private static Game QuartersGame()
    {
        var game = TestData.Game(split: GameSplitType.Quarters, durationMinutes: 60);
        game.AddPeriod(PeriodType.FirstQuarter);
        game.AddPeriod(PeriodType.SecondQuarter);
        game.AddPeriod(PeriodType.ThirdQuarter);
        game.AddPeriod(PeriodType.FourthQuarter);
        return game;
    }

    private static GamePeriod Period(Game game, PeriodType type) =>
        game.Periods.First(p => p.PeriodType == type);

    [Fact]
    public void Before_kick_off_the_clock_reads_zero()
    {
        var game = QuartersGame();

        var clock = MatchClockReport.Build(game, Period(game, PeriodType.FirstQuarter), 0);

        Assert.Equal(0, clock.Seconds);
        Assert.False(clock.IsInAdditionalTime);
    }

    [Fact]
    public void The_first_half_runs_from_zero()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;

        var clock = MatchClockReport.Build(game, Period(game, PeriodType.FirstQuarter), 600);

        Assert.Equal(600, clock.Seconds);
        Assert.Equal(0, clock.AdditionalSeconds);
    }

    [Fact]
    public void A_half_stops_at_the_length_of_a_half_and_the_overrun_is_reported_separately()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.SecondQuarter).StartedAtSeconds = 900;

        // 32 minutes into a 30-minute half.
        var clock = MatchClockReport.Build(game, Period(game, PeriodType.SecondQuarter), 32 * 60);

        Assert.Equal(30 * 60, clock.Seconds);
        Assert.Equal(2 * 60, clock.AdditionalSeconds);
        Assert.True(clock.IsInAdditionalTime);
    }

    /// <summary>The reason the cap exists: the second half must not inherit the overrun.</summary>
    [Fact]
    public void The_second_half_starts_at_half_the_match_however_long_the_first_half_took()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.SecondQuarter).StartedAtSeconds = 900;
        // The first half ran three minutes long before the whistle.
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 33 * 60;

        var kickOff = MatchClockReport.Build(game, Period(game, PeriodType.ThirdQuarter), 33 * 60);
        Assert.Equal(30 * 60, kickOff.Seconds);
        Assert.Equal(0, kickOff.AdditionalSeconds);

        // Five real minutes later the clock reads 35:00, not 38:00.
        var later = MatchClockReport.Build(game, Period(game, PeriodType.ThirdQuarter), 38 * 60);
        Assert.Equal(35 * 60, later.Seconds);
    }

    [Fact]
    public void A_quarter_boundary_does_not_restart_the_clock()
    {
        var game = QuartersGame();
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 33 * 60;
        Period(game, PeriodType.FourthQuarter).StartedAtSeconds = 48 * 60;

        // Q4 opened 15 minutes into the second half, so the clock reads 45:00 there.
        var clock = MatchClockReport.Build(game, Period(game, PeriodType.FourthQuarter), 48 * 60);

        Assert.Equal(45 * 60, clock.Seconds);
        Assert.Equal(0, clock.AdditionalSeconds);
    }

    [Fact]
    public void The_second_half_stops_at_full_time()
    {
        var game = QuartersGame();
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 30 * 60;
        Period(game, PeriodType.FourthQuarter).StartedAtSeconds = 45 * 60;

        var clock = MatchClockReport.Build(game, Period(game, PeriodType.FourthQuarter), 64 * 60);

        Assert.Equal(60 * 60, clock.Seconds);
        Assert.Equal(4 * 60, clock.AdditionalSeconds);
    }

    /// <summary>
    /// 45 minutes in two halves splits to 22:30 each. Working in minutes would round each half
    /// down to 22 and the clock would stop a minute short of full time.
    /// </summary>
    [Fact]
    public void An_odd_duration_does_not_lose_the_half_minute()
    {
        var game = TestData.Game(durationMinutes: 45);
        game.AddPeriod(PeriodType.FirstHalf);
        game.AddPeriod(PeriodType.SecondHalf);
        Period(game, PeriodType.FirstHalf).StartedAtSeconds = 0;
        Period(game, PeriodType.SecondHalf).StartedAtSeconds = 1350;

        var atTheBreak = MatchClockReport.Build(game, Period(game, PeriodType.FirstHalf), 1350);
        Assert.Equal(1350, atTheBreak.Seconds);              // 22:30

        var atFullTime = MatchClockReport.Build(game, Period(game, PeriodType.SecondHalf), 2700);
        Assert.Equal(2700, atFullTime.Seconds);              // 45:00
        Assert.Equal(0, atFullTime.AdditionalSeconds);
    }

    /// <summary>
    /// A half that has been played out stops counting minutes and starts counting them alongside,
    /// the way football writes 30+2 — so the minute never runs into the numbers the next half uses.
    /// </summary>
    [Fact]
    public void The_minute_stops_with_the_clock_and_additional_time_is_counted_beside_it()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.SecondQuarter).StartedAtSeconds = 900;

        var clock = MatchClockReport.Build(game, Period(game, PeriodType.SecondQuarter), 31 * 60 + 30);

        Assert.Equal(30 * 60, clock.Seconds);
        Assert.Equal(new MatchMinute(30, 2), clock.Minute);
        Assert.Equal("30+2", clock.Minute.ToString());
    }

    /// <summary>
    /// The whole point of the pair: a goal in first-half stoppage time and one just after the
    /// restart are a minute apart, and a single number would have written them 32 and 31.
    /// </summary>
    [Fact]
    public void A_stoppage_time_minute_comes_before_the_first_minute_of_the_next_half()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 32 * 60;

        var stoppage = MatchClockReport.Build(game, Period(game, PeriodType.FirstQuarter), 31 * 60);
        var afterTheBreak = MatchClockReport.Build(game, Period(game, PeriodType.ThirdQuarter), 32 * 60);

        Assert.Equal(new MatchMinute(30, 2), stoppage.Minute);
        Assert.Equal(new MatchMinute(31, 0), afterTheBreak.Minute);
        Assert.True(stoppage.Minute.CompareTo(afterTheBreak.Minute) < 0);
    }

    [Fact]
    public void The_first_minute_of_play_is_the_first_minute()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;

        MatchMinute MinuteAt(int seconds) =>
            MatchClockReport.Build(game, Period(game, PeriodType.FirstQuarter), seconds).Minute;

        Assert.Equal(new MatchMinute(1, 0), MinuteAt(0));
        Assert.Equal(new MatchMinute(1, 0), MinuteAt(59));
        Assert.Equal(new MatchMinute(2, 0), MinuteAt(60));
        Assert.Equal("2", MinuteAt(60).ToString());
    }

    [Fact]
    public void A_substitution_is_written_against_the_clock_its_own_half_was_showing()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        var secondHalf = Period(game, PeriodType.ThirdQuarter);
        secondHalf.StartedAtSeconds = 32 * 60;
        secondHalf.Id = 7;

        // Two minutes into a second half that kicked off 32 real minutes in: 33', not 35'.
        var sub = new GameSubstitution { GamePeriodId = 7, AtSeconds = 34 * 60 };
        Assert.Equal(new MatchMinute(33, 0), MatchClockReport.MinuteOf(game, sub));

        // A substitution whose period is not loaded still says something better than 1'.
        var orphan = new GameSubstitution { GamePeriodId = 99, AtSeconds = 34 * 60 };
        Assert.Equal(new MatchMinute(35, 0), MatchClockReport.MinuteOf(game, orphan));
    }

    [Fact]
    public void A_goal_recorded_without_a_minute_has_none_to_show()
    {
        Assert.Null(MatchClockReport.MinuteOf(new GameGoal()));
        Assert.Equal(new MatchMinute(35, 2),
            MatchClockReport.MinuteOf(new GameGoal { Minute = 35, AdditionalMinute = 2 }));
    }

    [Fact]
    public void A_game_with_no_duration_on_file_just_shows_the_time_running()
    {
        var game = TestData.Game(durationMinutes: 0);
        game.AddPeriod(PeriodType.FirstHalf);
        Period(game, PeriodType.FirstHalf).StartedAtSeconds = 0;

        var clock = MatchClockReport.Build(game, Period(game, PeriodType.FirstHalf), 600);

        Assert.Equal(600, clock.Seconds);
        Assert.False(clock.IsInAdditionalTime);
    }

    [Fact]
    public void Nothing_to_show_reads_as_before_kick_off()
    {
        Assert.Equal(MatchClock.BeforeKickOff, MatchClockReport.Build(QuartersGame(), null, 500));
    }
}
