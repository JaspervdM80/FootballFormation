namespace FootballFormation.Core.Reporting;

/// Always ours first — only the display reads the venue and decides whether the home side goes on the left.
public readonly record struct MatchScore(int Us, int Them);

/// The timeline lists events newest first, so a running total cannot be accumulated as it renders — this counts forwards once for the
/// whole match instead, keyed by goal id.
public static class ScoreProgressionReport
{

    public static IReadOnlyDictionary<int, MatchScore> Build(Game game)
    {
        var progression = new Dictionary<int, MatchScore>();
        var us = 0;
        var them = 0;

        // The order the live screen shows events in, read forwards. See LiveMatch.Timeline for why all three keys.
        var chronological = game.Goals
            .OrderBy(g => MatchClockReport.ElapsedOf(game, g))
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
