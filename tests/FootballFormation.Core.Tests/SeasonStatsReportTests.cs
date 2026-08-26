namespace FootballFormation.Core.Tests;

public class SeasonStatsReportTests
{
    private static readonly Player Anyone = TestData.Player(1, "Anyone");

    private static Game Result(int id, int home, int away, DateTime date)
    {
        var game = TestData.Game(id: id, date: date);
        game.MatchState = MatchState.Finished;
        game.ScoreHome = home;
        game.ScoreAway = away;
        return game;
    }

    [Fact]
    public void The_empty_report_is_the_answer_Build_gives_for_a_season_with_nothing_in_it()
    {
        var built = SeasonStatsReport.Build([], [], SeasonSquads.Empty);

        // A page renders this when its load failed, so it has to match what Build produces.
        Assert.Equal(built.Played, SeasonStats.Empty.Played);
        Assert.Equal(built.GoalsFor, SeasonStats.Empty.GoalsFor);
        Assert.Equal(built.GoalsAgainst, SeasonStats.Empty.GoalsAgainst);
        Assert.Empty(SeasonStats.Empty.Players);
        Assert.Empty(SeasonStats.Empty.Form);
    }

    [Fact]
    public void The_table_counts_only_finished_games()
    {
        var inProgress = Result(4, 5, 0, new DateTime(2026, 4, 1));
        inProgress.MatchState = MatchState.InProgress;

        var future = TestData.Game(id: 5, date: new DateTime(2026, 5, 1));

        var stats = SeasonStatsReport.Build([Anyone], [
            Result(1, 3, 1, new DateTime(2026, 1, 10)),
            Result(2, 2, 2, new DateTime(2026, 1, 17)),
            Result(3, 0, 4, new DateTime(2026, 1, 24)),
            inProgress,
            future
        ], SeasonSquads.Empty);

        Assert.Equal(3, stats.Played);
        Assert.Equal(1, stats.Won);
        Assert.Equal(1, stats.Drawn);
        Assert.Equal(1, stats.Lost);
        Assert.Equal(5, stats.GoalsFor);
        Assert.Equal(7, stats.GoalsAgainst);
        Assert.Equal(-2, stats.GoalDifference);
        Assert.Equal(33, stats.WinPercentage);
    }

    [Fact]
    public void Form_is_the_five_most_recent_results_newest_first()
    {
        var games = Enumerable.Range(1, 7)
            .Select(i => Result(i, i % 2, 0, new DateTime(2026, 1, i)))   // odd id = 1-0 win
            .ToList();

        var stats = SeasonStatsReport.Build([Anyone], games, SeasonSquads.Empty);

        Assert.Equal(5, stats.Form.Count);
        // Newest is game 7 (a win), then 6 (loss 0-0? no — 0-0 is a draw), 5 win, 4 draw, 3 win.
        Assert.Equal(
            [GameResult.Win, GameResult.Draw, GameResult.Win, GameResult.Draw, GameResult.Win],
            stats.Form);
    }

    [Fact]
    public void An_empty_season_reports_zeroes_rather_than_dividing_by_zero()
    {
        var stats = SeasonStatsReport.Build([Anyone], [], SeasonSquads.Empty);

        Assert.Equal(0, stats.Played);
        Assert.Equal(0, stats.WinPercentage);
        Assert.Empty(stats.Form);
        Assert.Single(stats.Players);
    }

    [Fact]
    public void Every_player_passed_in_gets_a_row_even_with_no_minutes()
    {
        var second = TestData.Player(2, "Second");

        var stats = SeasonStatsReport.Build([Anyone, second], [
            Result(1, 1, 0, new DateTime(2026, 1, 10))
        ], SeasonSquads.Empty);

        Assert.Equal([1, 2], stats.Players.Select(p => p.Player.Id));
        Assert.All(stats.Players, p => Assert.Equal(0, p.TotalMinutes));
    }
}
