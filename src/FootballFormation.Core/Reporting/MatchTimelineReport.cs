namespace FootballFormation.Core.Reporting;

/// Sorted on <paramref name="AtSeconds"/> first: the elapsed clock runs on across the break, so a first-half stoppage entry stays above
/// the restart without comparing scoreboard readings. <paramref name="Minute"/> is that scoreboard reading, display only.
public record MatchEvent(
    int AtSeconds, MatchMinute? Minute, PeriodType Half, DateTime RecordedAt, int Id,
    GameGoal? Goal, GameSubstitution? Substitution, GameInjury? Injury = null,
    MatchScore? Score = null, bool HalfTimeAbove = false);

/// The goals, substitutions and injuries of a match on one clock — built once here so the live screen and the finished-match result page
/// read the same list. Substitutions can be filtered out because heavy rotation buries the goals among them; an injury is never folded away.
public static class MatchTimelineReport
{
    /// <paramref name="newestFirst"/> is the live screen's order — latest on top; the finished-match result page reads the other way, kick-off first.
    public static List<MatchEvent> Build(Game game, bool includeSubstitutions, bool newestFirst)
    {
        // Counted forwards over the whole match, then looked up per goal — this list runs newest first, so accumulating while rendering
        // it would count down.
        var progression = ScoreProgressionReport.Build(game);

        var goals = game.Goals.Select(g =>
        {
            var at = MatchClockReport.ElapsedOf(game, g);
            return new MatchEvent(
                at,
                MatchClockReport.MinuteOf(game, g),
                MatchClockReport.HalfOf(game, g.GamePeriodId, at),
                g.RecordedAt, g.Id, g, null, Score: progression[g.Id]);
        });

        IEnumerable<MatchEvent> subs = includeSubstitutions
            ? game.Substitutions.Select(s => new MatchEvent(
                s.AtSeconds,
                MatchClockReport.MinuteOf(game, s),
                MatchClockReport.HalfOf(game, s.GamePeriodId, s.AtSeconds),
                s.RecordedAt, s.Id, null, s, game.InjuryFor(s)))
            : [];

        // Only the injuries nobody came on for; the rest are on their substitution's line.
        var injuries = game.Injuries
            .Where(i => !game.WasReplaced(i))
            .Select(i => new MatchEvent(
                i.AtSeconds,
                MatchClockReport.MinuteOf(game, i),
                MatchClockReport.HalfOf(game, i.GamePeriodId, i.AtSeconds),
                i.RecordedAt, i.Id, null, null, i));

        // A goal and the sub that followed it commonly share a second, so the entry time orders them as they happened. The id then
        // settles a double substitution, keeping the newest-first top entry the one an undo will remove.
        IEnumerable<MatchEvent> chronological = goals.Concat(subs).Concat(injuries)
            .OrderBy(e => e.AtSeconds)
            .ThenBy(e => e.RecordedAt)
            .ThenBy(e => e.Id);

        var ordered = (newestFirst ? chronological.Reverse() : chronological).ToList();

        // Marked here rather than in the markup, which renders one entry at a time and cannot see the one above it; lands between the halves either way.
        return [.. ordered.Select((e, i) =>
            i > 0 && ordered[i - 1].Half != e.Half ? e with { HalfTimeAbove = true } : e)];
    }
}
