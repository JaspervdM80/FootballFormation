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
    /// <summary>A goal off the touchline, placed by the elapsed match clock it was logged at.</summary>
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

    /// <summary>
    /// A goal in first-half stoppage time was scored before one just after the restart, and the
    /// running total follows that without anyone comparing scoreboard readings — the elapsed clock
    /// runs on across the break, where the scoreboard reads 30+2 and then 31 all over again.
    /// </summary>
    [Fact]
    public void A_stoppage_time_goal_is_counted_inside_the_half_it_was_scored_in()
    {
        // Two minutes past a 30-minute half, then a minute into a second half that kicked off at 32.
        var stoppage = Goal(1, 32);
        var afterTheBreak = Goal(2, 33, opponentGoal: true);
        afterTheBreak.RecordedAt = stoppage.RecordedAt.AddMinutes(16);

        var progression = ScoreProgressionReport.Build([afterTheBreak, stoppage]);

        Assert.Equal(new MatchScore(1, 0), progression[1]);
        Assert.Equal(new MatchScore(1, 1), progression[2]);
    }

    /// <summary>
    /// A goal typed in on the result page has no clock behind it, so the minute on the row is what
    /// places it — which is the only thing keeping goals recorded before the clock was stored in
    /// the order they have always had.
    /// </summary>
    [Fact]
    public void A_goal_with_only_a_minute_is_counted_in_that_minute()
    {
        var typedIn = new GameGoal { Id = 1, Minute = 40 };
        var logged = Goal(2, 10, opponentGoal: true);

        var progression = ScoreProgressionReport.Build([typedIn, logged]);

        Assert.Equal(new MatchScore(0, 1), progression[2]);
        Assert.Equal(new MatchScore(1, 1), progression[1]);
    }

    [Fact]
    public void A_match_with_no_goals_has_nothing_to_report()
    {
        Assert.Empty(ScoreProgressionReport.Build([]));
    }
}
