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

    private static PositionDevelopment BuildReport(params Player[] order)
    {
        var squads = SeasonSquads.Of(TestData.Squad(1, [PlayerA, PlayerB, PlayerC]));
        var game = Game();

        var playerStats = (order.Length > 0 ? order : [PlayerA, PlayerB, PlayerC])
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

    [Fact]
    public void Rows_come_out_in_shirt_number_order_regardless_of_input_order()
    {
        // B (shirt 2) passed in ahead of A (shirt 1) — the # column still reads 1, then 2.
        var report = BuildReport(PlayerB, PlayerA);

        Assert.Equal([PlayerA.Id, PlayerB.Id], report.Rows.Select(r => r.Player.Id));
    }

    /// Built by hand to put a stint too short to round up to a minute beside a real one — what a substitution seconds before the whistle
    /// produces.
    private static PlayerStats StatsWith(Player player, params PositionStat[] positions) =>
        new() { Player = player, Positions = [.. positions], Games = [] };

    private static PositionStat Position(PlayerPosition position, int minutes) =>
        new() { Position = position, Minutes = minutes, Percentage = 0 };

    [Fact]
    public void A_stint_too_short_to_count_a_minute_gets_no_column_of_its_own()
    {
        var report = PositionDevelopmentReport.Build(
            [StatsWith(PlayerA, Position(PlayerPosition.CM, 60), Position(PlayerPosition.RB, 0))]);

        // The cameo would otherwise put a "0'" column across every row in the grid.
        Assert.Equal([PlayerPosition.CM], report.Positions);
    }

    [Fact]
    public void A_cameo_in_a_second_position_does_not_clear_the_single_position_flag()
    {
        var report = PositionDevelopmentReport.Build(
            [StatsWith(PlayerA, Position(PlayerPosition.CM, 60), Position(PlayerPosition.RB, 0))]);

        // Twenty seconds at right back is not being rotated, and the grid cannot show it either.
        Assert.True(report.Rows.Single().IsSinglePosition);
    }

    [Fact]
    public void The_summary_counts_players_positions_and_who_never_moved()
    {
        // A played CM only; B split GK and ST; C never took the pitch.
        var report = BuildReport();

        Assert.Equal(2, report.PlayersUsed);
        Assert.Equal(3, report.PositionsUsed);
        Assert.Equal(1, report.SinglePositionPlayers);
    }

    [Fact]
    public void A_player_whose_only_minutes_round_away_is_left_out_entirely()
    {
        var report = PositionDevelopmentReport.Build([StatsWith(PlayerA, Position(PlayerPosition.CM, 0))]);

        Assert.Empty(report.Rows);
        Assert.Empty(report.Positions);
    }
}
