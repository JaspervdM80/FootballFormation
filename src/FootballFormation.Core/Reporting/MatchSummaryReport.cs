namespace FootballFormation.Core.Reporting;

/// Only a goal that <see cref="GameGoal.CountsForUs"/> gets a line: the rest are already in the scoreline and need no explanation in a
/// message this short. A null <paramref name="Minute"/> means the summary omits the bracket rather than printing (0').
public record MatchSummaryGoal(string ScorerName, string? AssistName, MatchMinute? Minute, PeriodType Half);

/// Built once so MatchResult and FormationOverview compose the same text. Scores arrive already in venue order, so a page has only
/// localized labels to add around them and no arithmetic to redo.
public record MatchSummary(
    VenueScore Score,
    IReadOnlyList<MatchSummaryGoal> Goals,
    IReadOnlyList<string> PublicComments);

/// Pure data, no localized text: Core carries no UI reference, so the words around these numbers are MatchSummaryTextBuilder's job.
public static class MatchSummaryReport
{
    /// Filters <paramref name="comments"/> to the public ones itself, whatever the viewer who copied it was allowed to see — a summary
    /// pasted into a group chat must never carry a private note along with it.
    public static MatchSummary Build(Game game, IReadOnlyList<GameComment> comments) =>
        new(
            game.ScoreboardOrder(),
            OurGoals(game),
            [.. comments.Where(c => c.IsPublic).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => c.Body)]);

    private static List<MatchSummaryGoal> OurGoals(Game game) =>
        [.. game.Goals
            .Where(g => g.CountsForUs)
            // The same order the result page's timeline uses — see ScoreProgressionReport.
            .OrderBy(g => MatchClockReport.ElapsedOf(game, g))
            .ThenBy(g => g.RecordedAt)
            .ThenBy(g => g.Id)
            .Select(g => new MatchSummaryGoal(
                g.Scorer?.DisplayName ?? "?",
                g.Assister?.DisplayName,
                MatchClockReport.MinuteOf(game, g),
                MatchClockReport.HalfOf(game, g.GamePeriodId, MatchClockReport.ElapsedOf(game, g))))];
}
