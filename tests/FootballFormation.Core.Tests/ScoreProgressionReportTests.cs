namespace FootballFormation.Core.Tests;

/// The timeline lists events newest first, so the totals have to be walked forwards — in the same order it puts them in backwards.
public class ScoreProgressionReportTests
{
    /// A 60-minute match — two 30-minute halves — carrying the goals it was scored in.
    private static Game Match(params GameGoal[] goals)
    {
        var game = TestData.Game(durationMinutes: 60);
        game.Goals.AddRange(goals);
        return game;
    }

    /// A goal off the touchline, placed by the elapsed match clock it was logged at.
    private static GameGoal Goal(
        int id, int minute, bool ownGoal = false, bool opponentGoal = false) =>
        new()
        {
            Id = id,
            AtSeconds = (minute - 1) * 60,
            IsOwnGoal = ownGoal,
            IsOpponentGoal = opponentGoal,
            RecordedAt = new DateTime(2026, 8, 11, 14, 0, minute, DateTimeKind.Utc)
        };

    [Fact]
    public void Each_goal_carries_the_score_it_made_it()
    {
        var goals = new List<GameGoal>
        {
            Goal(1, 5),
            Goal(2, 12, opponentGoal: true),
            Goal(3, 30)
        };

        var progression = ScoreProgressionReport.Build(Match([.. goals]));

        Assert.Equal(new MatchScore(1, 0), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
        Assert.Equal(new MatchScore(2, 1), progression[3]);
    }

    [Fact]
    public void An_own_goal_climbs_the_opponents_half_of_the_scoreline()
    {
        var goals = new List<GameGoal> { Goal(1, 20, ownGoal: true) };

        var progression = ScoreProgressionReport.Build(Match([.. goals]));

        Assert.Equal(new MatchScore(0, 1), progression[1]);
    }

    [Fact]
    public void The_goals_are_counted_in_the_order_they_were_scored_not_the_order_they_arrived()
    {
        var goals = new List<GameGoal> { Goal(2, 40), Goal(1, 10, opponentGoal: true) };

        var progression = ScoreProgressionReport.Build(Match([.. goals]));

        Assert.Equal(new MatchScore(0, 1), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
    }

    [Fact]
    public void Two_goals_in_the_same_minute_are_separated_by_when_they_were_entered()
    {
        // Typed in seconds apart, both in the 20th minute — the minute alone cannot say which was the equaliser.
        var first = Goal(7, 20, opponentGoal: true);
        var second = Goal(4, 20);
        second.RecordedAt = first.RecordedAt.AddSeconds(20);

        var progression = ScoreProgressionReport.Build(Match(second, first));

        Assert.Equal(new MatchScore(0, 1), progression[7]);
        Assert.Equal(new MatchScore(1, 1), progression[4]);
    }

    [Fact]
    public void The_last_goals_score_is_the_final_score()
    {
        var goals = new List<GameGoal> { Goal(1, 5), Goal(2, 25), Goal(3, 50, ownGoal: true) };

        var progression = ScoreProgressionReport.Build(Match([.. goals]));
        var final = progression[3];

        Assert.Equal(Game.CountOurGoals(goals), final.Us);
        Assert.Equal(Game.CountTheirGoals(goals), final.Them);
    }

    /// The elapsed clock runs on across the break, where the scoreboard reads 30+2 and then 31 again — so a stoppage-time goal keeps its
    /// place ahead of one just after the restart.
    [Fact]
    public void A_stoppage_time_goal_is_counted_inside_the_half_it_was_scored_in()
    {
        // Two minutes past a 30-minute half, then a minute into a second half that kicked off at 32.
        var stoppage = Goal(1, 32);
        var afterTheBreak = Goal(2, 33, opponentGoal: true);
        afterTheBreak.RecordedAt = stoppage.RecordedAt.AddMinutes(16);

        var progression = ScoreProgressionReport.Build(Match(afterTheBreak, stoppage));

        Assert.Equal(new MatchScore(1, 0), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
    }

    /// With no clock behind it the row's minute is all there is, which is what keeps goals recorded before the clock in their old order.
    [Fact]
    public void A_goal_with_only_a_minute_is_counted_in_that_minute()
    {
        var typedIn = new GameGoal { Id = 1, Minute = 40 };
        var logged = Goal(2, 10, opponentGoal: true);

        var progression = ScoreProgressionReport.Build(Match(typedIn, logged));

        Assert.Equal(new MatchScore(0, 1), progression[2]);
        Assert.Equal(new MatchScore(1, 1), progression[1]);
    }

    /// A typed-in minute is a scoreboard reading, so on a match whose first half over-ran, reading it as elapsed time filed a second-half
    /// goal ahead of one from first-half stoppage and handed both the wrong running score.
    [Fact]
    public void A_minute_typed_in_afterwards_is_placed_through_the_half_it_names()
    {
        // A first half whistled off three minutes long, so the second half kicks off at 33:00 while its scoreboard still starts at 30'.
        var game = Match();
        game.AddPeriod(PeriodType.FirstHalf);
        game.AddPeriod(PeriodType.SecondHalf);
        game.Periods.Single(p => p.PeriodType == PeriodType.FirstHalf).StartedAtSeconds = 0;
        game.Periods.Single(p => p.PeriodType == PeriodType.SecondHalf).StartedAtSeconds = 33 * 60;

        // Logged live two minutes into first-half stoppage — the scoreboard read 30+2.
        var stoppage = new GameGoal { Id = 1, AtSeconds = 32 * 60 };

        // Typed in afterwards as the 32nd minute, which is two minutes into the second half.
        var typedIn = new GameGoal { Id = 2, Minute = 32, IsOpponentGoal = true };

        game.Goals.AddRange([typedIn, stoppage]);

        var progression = ScoreProgressionReport.Build(game);

        Assert.Equal(new MatchScore(1, 0), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
    }

    [Fact]
    public void A_match_with_no_goals_has_nothing_to_report()
    {
        Assert.Empty(ScoreProgressionReport.Build(Match()));
    }
}
