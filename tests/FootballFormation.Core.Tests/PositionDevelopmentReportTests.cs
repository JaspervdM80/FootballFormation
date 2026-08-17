using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

public class PositionDevelopmentReportTests
{
    private static readonly Player PlayerA = TestData.Player(1, "A", PlayerPosition.CM, shirt: 1);
    private static readonly Player PlayerB = TestData.Player(2, "B", PlayerPosition.GK, shirt: 2);
    private static readonly Player PlayerC = TestData.Player(3, "C", PlayerPosition.ST, shirt: 3);

    /// <summary>A finished 60-minute game. A plays CM throughout; B switches from GK to ST at
    /// half-time; C is not in the lineup at all.</summary>
    private static Game Game()
    {
        var game = TestData.Game(id: 1, durationMinutes: 60);
        game.MatchState = MatchState.Finished;
        game.ScoreHome = 1;
        game.ScoreAway = 0;
        game.AddPeriod(PeriodType.FirstHalf,
            TestData.Starter(PlayerA.Id, PlayerPosition.CM), TestData.Starter(PlayerB.Id, PlayerPosition.GK));
        game.AddPeriod(PeriodType.SecondHalf,
            TestData.Starter(PlayerA.Id, PlayerPosition.CM), TestData.Starter(PlayerB.Id, PlayerPosition.ST));
        return game;
    }

    private static PositionDevelopment BuildReport()
    {
        var squads = SeasonSquads.Of(TestData.Squad(1, [PlayerA, PlayerB, PlayerC]));
        var game = Game();

        var playerStats = new[] { PlayerA, PlayerB, PlayerC }
            .Select(p => PlayerStatsReport.Build(p, [game], squads));

        return PositionDevelopmentReport.Build(playerStats);
    }

    [Fact]
    public void Positions_are_the_union_of_positions_played_by_the_squad_in_enum_order()
    {
        var report = BuildReport();

        // GK (0) before CM (13) before ST (29) — declared enum order, not the order players appear.
        Assert.Equal([PlayerPosition.GK, PlayerPosition.CM, PlayerPosition.ST], report.Positions);
    }

    [Fact]
    public void A_player_with_no_minutes_is_left_out_of_the_grid()
    {
        var report = BuildReport();

        Assert.DoesNotContain(report.Rows, r => r.Player.Id == PlayerC.Id);
        Assert.Equal(2, report.Rows.Count);
    }

    [Fact]
    public void A_player_who_played_only_one_position_all_season_is_flagged()
    {
        var report = BuildReport();
        var rowA = report.Rows.Single(r => r.Player.Id == PlayerA.Id);

        Assert.True(rowA.IsSinglePosition);
        Assert.Equal(60, rowA.Positions[PlayerPosition.CM].Minutes);
        Assert.Equal(100, rowA.Positions[PlayerPosition.CM].Percentage);
    }

    [Fact]
    public void A_player_split_across_two_positions_is_not_flagged_and_keeps_each_positions_share()
    {
        var report = BuildReport();
        var rowB = report.Rows.Single(r => r.Player.Id == PlayerB.Id);

        Assert.False(rowB.IsSinglePosition);
        Assert.Equal(30, rowB.Positions[PlayerPosition.GK].Minutes);
        Assert.Equal(30, rowB.Positions[PlayerPosition.ST].Minutes);
        Assert.Equal(50, rowB.Positions[PlayerPosition.GK].Percentage);
        Assert.Equal(50, rowB.Positions[PlayerPosition.ST].Percentage);
    }
}
