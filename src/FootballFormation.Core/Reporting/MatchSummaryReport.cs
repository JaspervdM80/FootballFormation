using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// One of our own goals, for the copyable summary. Own goals and the opponent's goals are already
/// counted into the scoreline and need no explanation in a message this short, so only a goal that
/// counts for us — see <see cref="GameGoal.CountsForUs"/> — gets a line here.
/// </summary>
/// <param name="Minute">Null for a goal typed in with none — the summary omits the bracket rather
/// than printing (0').</param>
public record MatchSummaryGoal(string ScorerName, string? AssistName, MatchMinute? Minute);

/// <summary>
/// Everything the copyable match summary needs, built once so <c>MatchResult</c> and
/// <c>FormationOverview</c> compose the same text rather than each reading the game a different
/// way. Scores are already in venue order — see <see cref="Game.ScoreboardOrder"/> — because the
/// page only has localized labels to add around them, not arithmetic to redo.
/// </summary>
public record MatchSummary(
    (int Home, int Away) Score,
    (int Home, int Away)? HalfTimeScore,
    IReadOnlyList<MatchSummaryGoal> Goals,
    IReadOnlyList<string> PublicComments);

/// <summary>
/// Builds the copyable match summary from a <see cref="Game"/> and its comments. Pure data, no
/// localized text — <c>Core</c> carries no UI reference, so the words around these numbers are the
/// page's job (<c>MatchSummaryTextBuilder</c> in <c>UI/Helpers</c>).
/// </summary>
public static class MatchSummaryReport
{
    /// <param name="game">With its goals and scorers loaded — <c>GameQueries.WithGoalsAndScorers</c>.</param>
    /// <param name="comments">This game's comments, of either visibility. Only the public ones make
    /// the summary — a visitor pasting this into a group chat must never carry a private note along
    /// with it, whatever the viewer who copied it was allowed to see.</param>
    public static MatchSummary Build(Game game, IReadOnlyList<GameComment> comments) =>
        new(
            game.ScoreboardOrder(),
            HalfTimeScore(game),
            OurGoals(game),
            [.. comments.Where(c => c.IsPublic).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => c.Body)]);

    private static List<MatchSummaryGoal> OurGoals(Game game) =>
        [.. game.Goals
            .Where(g => g.CountsForUs)
            // Same order the result page's timeline uses: the elapsed clock, which runs on across
            // the break, then when it was entered, then the id — see ScoreProgressionReport.
            .OrderBy(g => MatchClockReport.ElapsedOf(game, g))
            .ThenBy(g => g.RecordedAt)
            .ThenBy(g => g.Id)
            .Select(g => new MatchSummaryGoal(
                g.Scorer?.DisplayName ?? "?",
                g.Assister?.DisplayName,
                MatchClockReport.MinuteOf(game, g)))];

    /// <summary>
    /// The score at the break, or null when the match was never run live — a result typed in by
    /// hand has no half to report one for, and showing 0-0 would claim one it does not have.
    /// </summary>
    private static (int Home, int Away)? HalfTimeScore(Game game)
    {
        if (!game.HasActualTimings) return null;

        var firstHalf = game.Goals
            .Where(g => MatchClockReport.HalfOf(game, g.GamePeriodId, MatchClockReport.ElapsedOf(game, g))
                == PeriodType.FirstHalf)
            .ToList();

        return game.InVenueOrder(Game.CountOurGoals(firstHalf), Game.CountTheirGoals(firstHalf));
    }
}
