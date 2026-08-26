namespace FootballFormation.Core.Tests;

public class PlayingTimeReportTests
{
    private static readonly Player Starter = TestData.Player(1, "Starter", PlayerPosition.CM, shirt: 8);
    private static readonly Player Benched = TestData.Player(2, "Benched", PlayerPosition.ST, shirt: 9);
    private static readonly Player Absent = TestData.Player(3, "Absent", PlayerPosition.CB);

    private static (Game Game, Dictionary<int, List<GamePlayerPosition>> Lineups) TwoHalves()
    {
        var game = TestData.Game(durationMinutes: 60);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        var lineups = new Dictionary<int, List<GamePlayerPosition>>
        {
            [first.Id] = [TestData.Starter(1, PlayerPosition.CM, 5), TestData.Sub(2)],
            [second.Id] = [TestData.Starter(1, PlayerPosition.CM, 5), TestData.Starter(2, PlayerPosition.ST, 9)]
        };

        return (game, lineups);
    }

    [Fact]
    public void Minutes_and_share_come_from_periods_started()
    {
        var (game, lineups) = TwoHalves();

        var rows = PlayingTimeReport.Build(game, [Starter, Benched, Absent], lineups);

        var starter = rows.Single(r => r.PlayerId == 1);
        var benched = rows.Single(r => r.PlayerId == 2);
        var absent = rows.Single(r => r.PlayerId == 3);

        Assert.All(rows, r => Assert.False(r.IsActual));
        Assert.Equal(60, starter.TotalMinutes);
        Assert.Equal(100, starter.Percentage);
        Assert.Equal(30, benched.TotalMinutes);
        Assert.Equal(50, benched.Percentage);
        Assert.Equal(0, absent.TotalMinutes);
    }

    [Fact]
    public void A_game_that_was_run_live_is_timed_by_the_clock_not_by_the_plan()
    {
        var (game, lineups) = TwoHalves();
        var first = game.Periods[0];
        var second = game.Periods[1];

        // Both halves ran short: 25 and 20 minutes rather than the planned 30 each.
        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1500;
        second.StartedAtSeconds = 1500;
        second.EndedAtSeconds = 2700;

        // GameMinutesReport reads the periods' own lineups, which is what the live screen wrote.
        foreach (var (periodId, lineup) in lineups)
            game.Periods.Single(p => p.Id == periodId).PlayerPositions = [.. lineup];

        var rows = PlayingTimeReport.Build(game, [Starter, Benched, Absent], lineups);

        var starter = rows.Single(r => r.PlayerId == 1);
        var benched = rows.Single(r => r.PlayerId == 2);

        Assert.All(rows, r => Assert.True(r.IsActual));
        Assert.Equal(45, starter.TotalMinutes);      // every minute played, not the planned 60
        Assert.Equal(100, starter.Percentage);       // measured against the match that was played
        Assert.Equal(20, benched.TotalMinutes);      // the second half only
        Assert.Equal(44, benched.Percentage);
    }

    [Fact]
    public void A_substitution_is_credited_to_both_players_rather_than_the_period_to_one()
    {
        var game = TestData.Game(durationMinutes: 60);
        // The live screen rewrites the line-up in place, so it shows the FINAL occupants.
        var period = game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(2, PlayerPosition.CM, 5),
            TestData.Sub(1));

        period.StartedAtSeconds = 0;
        period.EndedAtSeconds = 1800;
        TestData.Substitution(game, period, offId: 1, onId: 2, atSeconds: 600, position: PlayerPosition.CM, slot: 5);

        var lineups = new Dictionary<int, List<GamePlayerPosition>> { [period.Id] = [.. period.PlayerPositions] };

        var rows = PlayingTimeReport.Build(game, [Starter, Benched], lineups);

        // The estimate could only ever have said 0 and 30; the substitution splits the half.
        Assert.Equal(10, rows.Single(r => r.PlayerId == 1).TotalMinutes);
        Assert.Equal(20, rows.Single(r => r.PlayerId == 2).TotalMinutes);
    }

    [Fact]
    public void A_period_still_being_played_credits_nobody_yet()
    {
        // This page has no match clock to close a running period off with, so a live half counts
        // once it has been whistled off — for the players and for the denominator alike.
        var (game, lineups) = TwoHalves();
        var first = game.Periods[0];

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = null;
        game.LivePeriodId = first.Id;
        first.PlayerPositions = [.. lineups[first.Id]];

        var rows = PlayingTimeReport.Build(game, [Starter, Benched], lineups);

        Assert.All(rows, r => Assert.True(r.IsActual));
        Assert.All(rows, r => Assert.Equal(0, r.TotalMinutes));
        Assert.All(rows, r => Assert.Equal(0, r.Percentage));
    }

    [Fact]
    public void Share_is_measured_against_playable_minutes_so_a_full_game_reads_100()
    {
        // 45 minutes in halves is 2 × 22.5, and playing both of them is the whole match.
        var game = TestData.Game(durationMinutes: 45);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        var lineups = new Dictionary<int, List<GamePlayerPosition>>
        {
            [first.Id] = [TestData.Starter(1, PlayerPosition.CM, 5)],
            [second.Id] = [TestData.Starter(1, PlayerPosition.CM, 5)]
        };

        var row = PlayingTimeReport.Build(game, [Starter], lineups).Single();

        Assert.Equal(45, row.TotalMinutes);
        Assert.Equal(100, row.Percentage);
    }

    [Fact]
    public void Each_period_records_status_position_and_fit()
    {
        var (game, lineups) = TwoHalves();
        var first = game.Periods[0];
        var second = game.Periods[1];

        var rows = PlayingTimeReport.Build(game, [Starter, Benched, Absent], lineups);

        var starter = rows.Single(r => r.PlayerId == 1);
        Assert.Equal(PeriodPlayStatus.Starting, starter.PeriodDetails[first.Id].Status);
        Assert.Equal(PlayerPosition.CM, starter.PeriodDetails[first.Id].Position);
        Assert.Equal(PositionFit.Preferred, starter.PeriodDetails[first.Id].Fit);

        var benched = rows.Single(r => r.PlayerId == 2);
        Assert.Equal(PeriodPlayStatus.Substitute, benched.PeriodDetails[first.Id].Status);
        Assert.Equal(PeriodPlayStatus.Starting, benched.PeriodDetails[second.Id].Status);

        var absent = rows.Single(r => r.PlayerId == 3);
        Assert.Equal(PeriodPlayStatus.NotPlaying, absent.PeriodDetails[first.Id].Status);
        Assert.Null(absent.PeriodDetails[first.Id].Position);
    }

    [Fact]
    public void Rows_are_ordered_by_minutes_then_shirt_number()
    {
        var (game, lineups) = TwoHalves();

        var rows = PlayingTimeReport.Build(game, [Absent, Benched, Starter], lineups);

        Assert.Equal([1, 2, 3], rows.Select(r => r.PlayerId));
    }

    [Fact]
    public void A_game_with_no_periods_yields_zero_rather_than_dividing_by_zero()
    {
        var row = PlayingTimeReport.Build(TestData.Game(), [Starter], new Dictionary<int, List<GamePlayerPosition>>())
            .Single();

        Assert.Equal(0, row.TotalMinutes);
        Assert.Equal(0, row.Percentage);
    }
}
