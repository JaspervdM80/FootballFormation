namespace FootballFormation.Core.Tests;

/// <summary>
/// The copyable match summary — the group-chat text, not the result page. Own goals and the
/// opponent's own regular goals stay out of the goal list on purpose: they are already in the
/// scoreline and a summary this short has no room to explain them.
/// </summary>
public class MatchSummaryReportTests
{
    private static Game Match(
        bool isHomeGame = true, int? scoreHome = null, int? scoreAway = null, int durationMinutes = 60)
    {
        var game = TestData.Game(durationMinutes: durationMinutes);
        game.IsHomeGame = isHomeGame;
        game.ScoreHome = scoreHome;
        game.ScoreAway = scoreAway;
        return game;
    }

    private static GameComment Comment(string body, bool isPublic, DateTime? createdAt = null) =>
        new() { Body = body, IsPublic = isPublic, CreatedAt = createdAt ?? DateTime.UtcNow };

    [Fact]
    public void Our_goals_carry_the_scorer_and_the_minute()
    {
        var game = Match(scoreHome: 1, scoreAway: 0);
        game.Goals.Add(new GameGoal { ScorerId = 1, Scorer = TestData.Player(1, "Sanne"), Minute = 12 });

        var summary = MatchSummaryReport.Build(game, []);

        var goal = Assert.Single(summary.Goals);
        Assert.Equal("Sanne", goal.ScorerName);
        Assert.Null(goal.AssistName);
        Assert.Equal(12, goal.Minute!.Value.Minute);
    }

    [Fact]
    public void An_own_goal_does_not_appear_among_our_scorers()
    {
        var game = Match(scoreHome: 0, scoreAway: 1);
        game.Goals.Add(new GameGoal
        {
            ScorerId = 1,
            Scorer = TestData.Player(1, "Sanne"),
            IsOwnGoal = true,
            Minute = 20
        });

        var summary = MatchSummaryReport.Build(game, []);

        Assert.Empty(summary.Goals);
        Assert.Equal(new VenueScore(0, 1), summary.Score);
    }

    [Fact]
    public void An_opponent_goal_does_not_appear_among_our_scorers()
    {
        var game = Match(scoreHome: 0, scoreAway: 1);
        game.Goals.Add(new GameGoal { IsOpponentGoal = true, Minute = 40 });

        var summary = MatchSummaryReport.Build(game, []);

        Assert.Empty(summary.Goals);
        Assert.Equal(new VenueScore(0, 1), summary.Score);
    }

    [Fact]
    public void A_goal_with_no_minute_is_still_listed_with_no_minute()
    {
        var game = Match(scoreHome: 1, scoreAway: 0);
        game.Goals.Add(new GameGoal { ScorerId = 1, Scorer = TestData.Player(1, "Fleur") });

        var summary = MatchSummaryReport.Build(game, []);

        var goal = Assert.Single(summary.Goals);
        Assert.Null(goal.Minute);
    }

    [Fact]
    public void An_assist_rides_along_on_the_same_goal_line()
    {
        var game = Match(scoreHome: 1, scoreAway: 0);
        game.Goals.Add(new GameGoal
        {
            ScorerId = 1,
            Scorer = TestData.Player(1, "Fleur"),
            AssisterId = 2,
            Assister = TestData.Player(2, "Lotte"),
            Minute = 34
        });

        var summary = MatchSummaryReport.Build(game, []);

        var goal = Assert.Single(summary.Goals);
        Assert.Equal("Fleur", goal.ScorerName);
        Assert.Equal("Lotte", goal.AssistName);
    }

    [Fact]
    public void An_away_fixture_puts_the_opponent_first()
    {
        // ScoreHome/ScoreAway are always us/them; away is the one flip to the venue order.
        var game = Match(isHomeGame: false, scoreHome: 3, scoreAway: 1);

        var summary = MatchSummaryReport.Build(game, []);

        Assert.Equal(new VenueScore(1, 3), summary.Score);
    }

    [Fact]
    public void A_match_with_no_scorers_recorded_still_reports_the_score()
    {
        var game = Match(scoreHome: 2, scoreAway: 1);

        var summary = MatchSummaryReport.Build(game, []);

        Assert.Empty(summary.Goals);
        Assert.Equal(new VenueScore(2, 1), summary.Score);
    }

    [Fact]
    public void A_goal_reports_the_half_it_was_scored_in()
    {
        var game = Match(scoreHome: 2, scoreAway: 1);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);
        first.StartedAtSeconds = 0;
        second.StartedAtSeconds = 1800;

        game.Goals.Add(new GameGoal
        {
            GamePeriodId = first.Id, AtSeconds = 600, ScorerId = 1, Scorer = TestData.Player(1, "Fleur")
        });
        game.Goals.Add(new GameGoal { GamePeriodId = second.Id, AtSeconds = 2000, IsOpponentGoal = true });
        game.Goals.Add(new GameGoal
        {
            GamePeriodId = second.Id, AtSeconds = 2400, ScorerId = 1, Scorer = TestData.Player(1, "Fleur")
        });

        var summary = MatchSummaryReport.Build(game, []);

        Assert.Equal([PeriodType.FirstHalf, PeriodType.SecondHalf], summary.Goals.Select(g => g.Half));
    }

    [Fact]
    public void A_game_never_run_live_reports_every_goal_as_first_half()
    {
        var game = Match(scoreHome: 2, scoreAway: 0);
        game.Goals.Add(new GameGoal { ScorerId = 1, Scorer = TestData.Player(1, "Fleur"), Minute = 12 });
        game.Goals.Add(new GameGoal { ScorerId = 1, Scorer = TestData.Player(1, "Fleur"), Minute = 50 });

        var summary = MatchSummaryReport.Build(game, []);

        Assert.All(summary.Goals, g => Assert.Equal(PeriodType.FirstHalf, g.Half));
    }

    [Fact]
    public void Only_public_comments_make_the_summary()
    {
        var game = Match(scoreHome: 1, scoreAway: 0);
        var comments = new List<GameComment>
        {
            Comment("Private note about substitutions", isPublic: false),
            Comment("Great team performance", isPublic: true)
        };

        var summary = MatchSummaryReport.Build(game, comments);

        Assert.Equal(["Great team performance"], summary.PublicComments);
    }

    [Fact]
    public void Comments_are_reported_oldest_first()
    {
        var game = Match(scoreHome: 0, scoreAway: 0);
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var comments = new List<GameComment>
        {
            Comment("Second", true, now.AddMinutes(5)),
            Comment("First", true, now)
        };

        var summary = MatchSummaryReport.Build(game, comments);

        Assert.Equal(["First", "Second"], summary.PublicComments);
    }
}
