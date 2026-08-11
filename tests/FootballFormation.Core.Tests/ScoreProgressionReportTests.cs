using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The score each goal made it. The live timeline lists events newest first, so the totals cannot
/// be counted off the list as it renders — they have to be walked forwards, in the same order the
/// timeline puts events in backwards.
/// </summary>
public class ScoreProgressionReportTests
{
    private static GameGoal Goal(int id, int minute, bool ownGoal = false, bool opponentGoal = false) =>
        new()
        {
            Id = id,
            Minute = minute,
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

        var progression = ScoreProgressionReport.Build(goals);

        Assert.Equal(new MatchScore(1, 0), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
        Assert.Equal(new MatchScore(2, 1), progression[3]);
    }

    [Fact]
    public void An_own_goal_climbs_the_opponents_half_of_the_scoreline()
    {
        var goals = new List<GameGoal> { Goal(1, 20, ownGoal: true) };

        var progression = ScoreProgressionReport.Build(goals);

        Assert.Equal(new MatchScore(0, 1), progression[1]);
    }

    [Fact]
    public void The_goals_are_counted_in_the_order_they_were_scored_not_the_order_they_arrived()
    {
        var goals = new List<GameGoal> { Goal(2, 40), Goal(1, 10, opponentGoal: true) };

        var progression = ScoreProgressionReport.Build(goals);

        Assert.Equal(new MatchScore(0, 1), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
    }

    [Fact]
    public void Two_goals_in_the_same_minute_are_separated_by_when_they_were_entered()
    {
        // Typed in seconds apart, both logged as the 20th minute — the minute alone cannot say
        // which was the equaliser and which put us ahead.
        var first = Goal(7, 20, opponentGoal: true);
        var second = Goal(4, 20);
        second.RecordedAt = first.RecordedAt.AddSeconds(20);

        var progression = ScoreProgressionReport.Build([second, first]);

        Assert.Equal(new MatchScore(0, 1), progression[7]);
        Assert.Equal(new MatchScore(1, 1), progression[4]);
    }

    [Fact]
    public void The_last_goals_score_is_the_final_score()
    {
        var goals = new List<GameGoal> { Goal(1, 5), Goal(2, 25), Goal(3, 50, ownGoal: true) };

        var progression = ScoreProgressionReport.Build(goals);
        var final = progression[3];

        Assert.Equal(Game.CountOurGoals(goals), final.Us);
        Assert.Equal(Game.CountTheirGoals(goals), final.Them);
    }

    [Fact]
    public void A_match_with_no_goals_has_nothing_to_report()
    {
        Assert.Empty(ScoreProgressionReport.Build([]));
    }
}
