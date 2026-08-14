using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// A scoreline, always ours first. Only the display decides whether the home side goes on the
/// left — see the live screen's scoreboard, which reads the venue and this record does not.
/// </summary>
public readonly record struct MatchScore(int Us, int Them);

/// <summary>
/// The score as it stood immediately after each goal, keyed by goal id.
/// <para>
/// The timeline lists events newest first, so a running total cannot be accumulated as the list
/// renders — it has to be counted in the order the goals were actually scored, which is what this
/// does once for the whole match.
/// </para>
/// </summary>
public static class ScoreProgressionReport
{
    /// <param name="goals">Every goal in the match, in any order.</param>
    public static IReadOnlyDictionary<int, MatchScore> Build(IEnumerable<GameGoal> goals)
    {
        var progression = new Dictionary<int, MatchScore>();
        var us = 0;
        var them = 0;

        // The order the live screen shows events in, read forwards: the elapsed match clock first,
        // which runs on across the break and so keeps a stoppage-time goal inside the half it was
        // scored in, then the moment it was entered, then the id. See LiveMatch.Timeline for why
        // all three.
        var chronological = goals
            .OrderBy(g => g.TimelineSeconds)
            .ThenBy(g => g.RecordedAt)
            .ThenBy(g => g.Id);

        foreach (var goal in chronological)
        {
            if (goal.CountsForUs) us++;
            else them++;

            progression[goal.Id] = new MatchScore(us, them);
        }

        return progression;
    }
}
