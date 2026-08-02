using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The calculation every minutes figure in the app funnels through — the live bench table, the
/// player report and the season fairness bars all read it, so an error here is invisible until a
/// whole season's statistics are wrong.
/// </summary>
public class GameMinutesReportTests
{
    [Fact]
    public void A_game_never_run_live_credits_the_planned_lineup_a_full_period_each()
    {
        var game = TestData.Game(durationMinutes: 60);                 // 2 × 30 min
        game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(3));
        game.AddPeriod(PeriodType.SecondHalf,
            TestData.Starter(1, PlayerPosition.GK, 0),
            TestData.Starter(3, PlayerPosition.CM, 5));

        var minutes = GameMinutesReport.Build(game);

        Assert.False(minutes.IsActual);
        Assert.Equal(3600, minutes.SecondsFor(1));   // both halves
        Assert.Equal(1800, minutes.SecondsFor(2));   // first only
        Assert.Equal(1800, minutes.SecondsFor(3));   // benched, then played
        // Everyone named in a lineup is known, even with zero seconds.
        Assert.Equal([1, 2, 3], minutes.PlayerIds.Order());
    }

    [Fact]
    public void Real_timings_beat_the_plan_once_a_game_has_been_run_live()
    {
        var game = TestData.Game(durationMinutes: 60);
        var first = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(1, PlayerPosition.CM, 5));

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1200;                 // whistled off early, 20 min

        var minutes = GameMinutesReport.Build(game);

        Assert.True(minutes.IsActual);
        Assert.Equal(1200, minutes.SecondsFor(1));   // not the planned 1800
    }

    [Fact]
    public void A_period_that_was_never_kicked_off_credits_nobody()
    {
        var game = TestData.Game();
        var first = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
        game.AddPeriod(PeriodType.SecondHalf, TestData.Starter(2, PlayerPosition.CM, 5));

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;

        var minutes = GameMinutesReport.Build(game);

        // The second half was abandoned. Crediting its lineup would invent playing time.
        Assert.Equal(1800, minutes.SecondsFor(1));
        Assert.Equal(0, minutes.SecondsFor(2));
        Assert.Contains(2, minutes.PlayerIds);
    }

    [Fact]
    public void A_substitution_splits_the_period_between_both_players()
    {
        var game = TestData.Game();
        // The lineup records the FINAL occupants — player 2 is on the pitch, player 1 is benched,
        // because SubstituteAsync rewrote it in place. Rewinding the sub is the only way back.
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(1));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 2, atSeconds: 600, position: PlayerPosition.CM, slot: 5);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(600, minutes.SecondsFor(1));    // started, came off on 10'
        Assert.Equal(1200, minutes.SecondsFor(2));   // came on, played out the half
    }

    [Fact]
    public void Minutes_are_attributed_to_the_position_actually_held()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.ST, 9),
            TestData.Sub(1));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 2, atSeconds: 600, position: PlayerPosition.ST, slot: 9);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(600, minutes.PositionsFor(1)[PlayerPosition.ST]);
        Assert.Equal(1200, minutes.PositionsFor(2)[PlayerPosition.ST]);
    }

    [Fact]
    public void Two_substitutions_in_the_same_second_subtract_no_time()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(3, PlayerPosition.CM, 5),
            TestData.Starter(4, PlayerPosition.ST, 9),
            TestData.Sub(1),
            TestData.Sub(2));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 3, atSeconds: 900, position: PlayerPosition.CM, slot: 5);
        TestData.Substitution(game, period, offId: 2, onId: 4, atSeconds: 900, position: PlayerPosition.ST, slot: 9);

        var minutes = GameMinutesReport.Build(game);

        Assert.Equal(900, minutes.SecondsFor(1));
        Assert.Equal(900, minutes.SecondsFor(2));
        Assert.Equal(900, minutes.SecondsFor(3));
        Assert.Equal(900, minutes.SecondsFor(4));
    }

    [Fact]
    public void A_running_period_is_closed_off_by_the_current_clock()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = null;
        game.LivePeriodId = period.Id;

        var minutes = GameMinutesReport.Build(game, elapsedSeconds: 754);

        Assert.Equal(754, minutes.SecondsFor(1));
        Assert.Equal([1], minutes.OnPitchNow.Order());
    }

    [Fact]
    public void Nobody_is_on_the_pitch_when_no_period_is_live()
    {
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf, TestData.Starter(1, PlayerPosition.CM, 5));
        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;

        Assert.Empty(GameMinutesReport.Build(game, 1800).OnPitchNow);
    }

    [Fact]
    public void A_player_brought_on_who_was_never_in_the_lineup_is_still_counted()
    {
        // Someone who turned up late: SubstituteAsync adds them rather than refusing the change.
        var game = TestData.Game();
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(1));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 2, atSeconds: 300, position: PlayerPosition.CM, slot: 5);

        var minutes = GameMinutesReport.Build(game);

        Assert.Contains(2, minutes.PlayerIds);
        Assert.Equal(1500, minutes.SecondsFor(2));
    }

    [Fact]
    public void An_unknown_player_reports_zero_rather_than_throwing()
    {
        var minutes = GameMinutesReport.Build(TestData.Game());

        Assert.Equal(0, minutes.SecondsFor(999));
        Assert.Empty(minutes.PositionsFor(999));
    }
}
