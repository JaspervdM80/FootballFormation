using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

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

        Assert.Equal(60, starter.TotalMinutes);
        Assert.Equal(100, starter.Percentage);
        Assert.Equal(30, benched.TotalMinutes);
        Assert.Equal(50, benched.Percentage);
        Assert.Equal(0, absent.TotalMinutes);
    }

    [Fact]
    public void Share_is_measured_against_playable_minutes_so_a_full_game_reads_100()
    {
        // 45 minutes in halves is 2 × 22 — the integer split drops a minute. Playing every period
        // must still read 100%, not 98%.
        var game = TestData.Game(durationMinutes: 45);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        var lineups = new Dictionary<int, List<GamePlayerPosition>>
        {
            [first.Id] = [TestData.Starter(1, PlayerPosition.CM, 5)],
            [second.Id] = [TestData.Starter(1, PlayerPosition.CM, 5)]
        };

        var row = PlayingTimeReport.Build(game, [Starter], lineups).Single();

        Assert.Equal(44, row.TotalMinutes);
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
