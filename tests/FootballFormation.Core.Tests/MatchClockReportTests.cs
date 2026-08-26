namespace FootballFormation.Core.Tests;

/// The scoreboard clock, which is deliberately not the elapsed time: a half stops at the length of a half, and the second half starts at
/// half the match duration however long the first really took.
public class MatchClockReportTests
{
    /// A 60-minute game in quarters: two 30-minute halves, Q1/Q2 then Q3/Q4.
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

    /// The reason the cap exists: the second half must not inherit the overrun.
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

    /// 45 minutes in two halves splits to 22:30 each — working in minutes would round each half down and stop the clock short of full time.
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

    /// A played-out half counts alongside rather than on, the way football writes 30+2, so its minutes never run into the next half's.
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

    /// The whole point of the pair: a goal in first-half stoppage time and one just after the restart are a minute apart, and a single
    /// counted-on number would have written them 32 and 31.
    [Fact]
    public void A_stoppage_time_minute_stays_inside_the_half_it_was_played_in()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 32 * 60;

        var stoppage = MatchClockReport.Build(game, Period(game, PeriodType.FirstQuarter), 31 * 60);
        var afterTheBreak = MatchClockReport.Build(game, Period(game, PeriodType.ThirdQuarter), 32 * 60);

        Assert.Equal(new MatchMinute(30, 2), stoppage.Minute);
        Assert.Equal("30+2", stoppage.Minute.ToString());
        Assert.Equal(new MatchMinute(31, 0), afterTheBreak.Minute);
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

        // An injury is placed off the same clock, so the two read the same minute on one timeline.
        var injury = new GameInjury { GamePeriodId = 7, AtSeconds = 34 * 60 };
        Assert.Equal(new MatchMinute(33, 0), MatchClockReport.MinuteOf(game, injury));
    }

    /// A goal is placed off its own half's clock like a substitution is, which is the point of storing the pair rather than the minute.
    [Fact]
    public void A_goal_is_written_against_the_clock_its_own_half_was_showing()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        var secondHalf = Period(game, PeriodType.ThirdQuarter);
        secondHalf.StartedAtSeconds = 32 * 60;
        secondHalf.Id = 7;

        var goal = new GameGoal { GamePeriodId = 7, AtSeconds = 34 * 60 };
        Assert.Equal(new MatchMinute(33, 0), MatchClockReport.MinuteOf(game, goal));

        // Played out and still going: the reading a scoreboard would show, not a counted-on 32.
        var stoppage = new GameGoal { GamePeriodId = 7, AtSeconds = 63 * 60 };
        Assert.Equal(new MatchMinute(60, 2), MatchClockReport.MinuteOf(game, stoppage));
    }

    /// Correcting a half's timings corrects the goals scored in it — what deriving the minute buys over storing it.
    [Fact]
    public void Moving_a_halfs_kick_off_moves_the_goals_scored_in_it()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        var secondHalf = Period(game, PeriodType.ThirdQuarter);
        secondHalf.Id = 7;
        secondHalf.StartedAtSeconds = 32 * 60;

        // Eight minutes into a second half whose clock starts at 30: the 39th minute.
        var goal = new GameGoal { GamePeriodId = 7, AtSeconds = 40 * 60 };
        Assert.Equal(new MatchMinute(39, 0), MatchClockReport.MinuteOf(game, goal));

        // The half kicked off three minutes later than recorded, so the goal moves with it. A stored minute would have stayed put and
        // disagreed with the substitutions around it.
        secondHalf.StartedAtSeconds = 35 * 60;
        Assert.Equal(new MatchMinute(36, 0), MatchClockReport.MinuteOf(game, goal));
    }

    [Fact]
    public void A_goal_with_no_clock_behind_it_falls_back_to_the_minute_on_the_row()
    {
        var game = QuartersGame();

        // Typed in on the result page, and every goal logged before the clock reading was stored.
        Assert.Equal(new MatchMinute(35, 0),
            MatchClockReport.MinuteOf(game, new GameGoal { Minute = 35 }));

        // Neither one nor the other: the result page allows a goal with no minute at all.
        Assert.Null(MatchClockReport.MinuteOf(game, new GameGoal()));
    }

    /// The timeline orders on elapsed seconds, so a hand-typed minute has to be converted onto that scale. The two agree only while the
    /// halves run to length; this first half is three minutes long.
    [Fact]
    public void A_typed_in_minute_is_converted_onto_the_elapsed_clock_through_its_half()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Period(game, PeriodType.ThirdQuarter).StartedAtSeconds = 33 * 60;

        int Elapsed(int minute) => MatchClockReport.ElapsedOf(game, new GameGoal { Minute = minute });

        // First half: the scoreboard is the elapsed clock, because it kicked off at zero.
        Assert.Equal(0, Elapsed(1));
        Assert.Equal(20 * 60, Elapsed(21));

        // 31' is the first minute after a scoreboard restart at 30, which really happened at 33:00. Read as elapsed seconds it would
        // land before a goal scored in first-half stoppage time.
        Assert.Equal(33 * 60, Elapsed(31));
        Assert.Equal(35 * 60, Elapsed(33));
        Assert.Equal(PeriodType.SecondHalf, MatchClockReport.HalfOf(game, null, Elapsed(31)));

        // The clock on the row always wins — it is the reading, not a reconstruction of one.
        Assert.Equal(1234, MatchClockReport.ElapsedOf(game, new GameGoal { AtSeconds = 1234, Minute = 5 }));

        // Nothing at all: the top of the match, which is where a goal with no minute has always sat.
        Assert.Equal(0, MatchClockReport.ElapsedOf(game, new GameGoal()));
    }

    /// A match nobody ran from the touchline has no timings to convert through, so the typed minutes keep the only order they have.
    [Fact]
    public void Typed_in_minutes_stand_on_their_own_when_no_half_was_ever_kicked_off()
    {
        var game = QuartersGame();

        Assert.Equal(0, MatchClockReport.ElapsedOf(game, new GameGoal { Minute = 1 }));
        Assert.Equal(30 * 60, MatchClockReport.ElapsedOf(game, new GameGoal { Minute = 31 }));
        Assert.Equal(59 * 60, MatchClockReport.ElapsedOf(game, new GameGoal { Minute = 60 }));
    }

    /// A goal typed in by hand knows only a clock reading, so the second half's kick-off is the line it falls one side of.
    [Fact]
    public void An_event_belongs_to_the_half_its_line_up_played_or_to_the_side_of_the_restart_it_falls()
    {
        var game = QuartersGame();
        Period(game, PeriodType.FirstQuarter).StartedAtSeconds = 0;
        var secondHalf = Period(game, PeriodType.ThirdQuarter);
        secondHalf.Id = 7;
        secondHalf.StartedAtSeconds = 32 * 60;

        // Q4 is planned for the middle of the second half, and is still the second half.
        var fourth = Period(game, PeriodType.FourthQuarter);
        fourth.Id = 9;

        Assert.Equal(PeriodType.SecondHalf, MatchClockReport.HalfOf(game, 7, 34 * 60));
        Assert.Equal(PeriodType.SecondHalf, MatchClockReport.HalfOf(game, 9, 34 * 60));
        Assert.Equal(PeriodType.FirstHalf, MatchClockReport.HalfOf(game, null, 31 * 60));
        Assert.Equal(PeriodType.SecondHalf, MatchClockReport.HalfOf(game, null, 32 * 60));

        // Nothing kicked off after the break, so there is no line to be the far side of.
        secondHalf.StartedAtSeconds = null;
        Assert.Equal(PeriodType.FirstHalf, MatchClockReport.HalfOf(game, null, 55 * 60));
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
