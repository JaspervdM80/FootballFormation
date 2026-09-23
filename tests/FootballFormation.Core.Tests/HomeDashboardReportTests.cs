namespace FootballFormation.Core.Tests;

public class HomeDashboardReportTests
{
    private static readonly DateTime Today = new(2026, 9, 23);

    private static Game Fixture(int id, DateTime date) => TestData.Game(id: id, date: date);

    private static Game Played(int id, DateTime date, int us, int them)
    {
        var game = TestData.Game(id: id, date: date);
        game.MatchState = MatchState.Finished;
        game.ScoreHome = us;
        game.ScoreAway = them;
        return game;
    }

    private static GameGoal GoalBy(Player scorer) =>
        new() { ScorerId = scorer.Id, Scorer = scorer };

    [Fact]
    public void No_games_leave_every_card_empty()
    {
        var dashboard = HomeDashboardReport.Build([], Today);

        Assert.Null(dashboard.NextGame);
        Assert.Null(dashboard.LastGame);
        Assert.Empty(dashboard.LastScorers);
        Assert.Equal(0, dashboard.Record.Played);
    }

    [Fact]
    public void Only_future_games_give_the_soonest_as_next_and_no_result()
    {
        var dashboard = HomeDashboardReport.Build(
            [Fixture(1, Today.AddDays(14)), Fixture(2, Today.AddDays(7)), Fixture(3, Today.AddDays(21))],
            Today);

        Assert.Equal(2, dashboard.NextGame?.Id);
        Assert.Null(dashboard.LastGame);
        Assert.Equal(0, dashboard.Record.Played);
    }

    [Fact]
    public void Only_past_games_give_the_latest_result_and_no_next_match()
    {
        var dashboard = HomeDashboardReport.Build(
            [Played(1, Today.AddDays(-7), 2, 1), Played(2, Today.AddDays(-21), 0, 3), Played(3, Today.AddDays(-14), 1, 1)],
            Today);

        Assert.Null(dashboard.NextGame);
        Assert.Equal(1, dashboard.LastGame?.Id);
        Assert.Equal((3, 1, 1, 1), (dashboard.Record.Played, dashboard.Record.Won, dashboard.Record.Drawn, dashboard.Record.Lost));
        Assert.Equal(-2, dashboard.Record.GoalDifference);
        Assert.Equal([GameResult.Win, GameResult.Draw, GameResult.Loss], dashboard.Record.Form);
    }

    /// The match-day banner already shows today's fixture, so the card moves on to the one after it rather than repeating it.
    [Fact]
    public void A_game_today_is_not_next_until_it_is_played_and_then_it_is_the_last_result()
    {
        var today = Fixture(1, Today.AddHours(10));
        var games = new[] { today, Fixture(2, Today.AddDays(7)) };

        var before = HomeDashboardReport.Build(games, Today.AddHours(8));
        Assert.Equal(2, before.NextGame?.Id);
        Assert.Null(before.LastGame);

        today.MatchState = MatchState.Finished;
        today.ScoreHome = 3;
        today.ScoreAway = 0;

        var after = HomeDashboardReport.Build(games, Today.AddHours(12));
        Assert.Equal(2, after.NextGame?.Id);
        Assert.Equal(1, after.LastGame?.Id);
    }

    /// A live score is written as the goals go in, so a match being played must not read as a result yet.
    [Fact]
    public void A_match_in_progress_is_not_the_last_result()
    {
        var live = Fixture(2, Today);
        live.MatchState = MatchState.InProgress;
        live.ScoreHome = 1;
        live.ScoreAway = 0;

        var dashboard = HomeDashboardReport.Build([Played(1, Today.AddDays(-7), 0, 0), live], Today);

        Assert.Equal(1, dashboard.LastGame?.Id);
    }

    [Fact]
    public void A_past_game_never_played_is_neither_next_nor_a_result()
    {
        var dashboard = HomeDashboardReport.Build([Fixture(1, Today.AddDays(-7))], Today);

        Assert.Null(dashboard.NextGame);
        Assert.Null(dashboard.LastGame);
    }

    [Fact]
    public void Two_results_on_one_day_pick_the_one_the_form_guide_leads_with()
    {
        var dashboard = HomeDashboardReport.Build(
            [Played(2, Today.AddDays(-1), 1, 0), Played(1, Today.AddDays(-1), 0, 1)],
            Today);

        Assert.Equal(1, dashboard.LastGame?.Id);
        Assert.Equal(GameResult.Loss, dashboard.Record.Form[0]);
    }

    [Fact]
    public void Our_scorers_are_listed_most_goals_first_without_own_goals_or_theirs()
    {
        var anna = TestData.Player(1, "Anna");
        var bo = TestData.Player(2, "Bo");
        var cas = TestData.Player(3, "Cas");

        var game = Played(1, Today.AddDays(-7), 3, 2);
        game.Goals =
        [
            GoalBy(bo),
            GoalBy(anna),
            GoalBy(bo),
            new GameGoal { ScorerId = cas.Id, Scorer = cas, IsOwnGoal = true },
            new GameGoal { IsOpponentGoal = true }
        ];

        var dashboard = HomeDashboardReport.Build([game], Today);

        Assert.Equal([("Bo", 2), ("Anna", 1)], dashboard.LastScorers.Select(s => (s.Scorer.FirstName, s.Goals)));
    }
}
