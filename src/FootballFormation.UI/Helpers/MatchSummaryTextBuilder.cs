using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Turns a <see cref="MatchSummary"/> into the plain text a copy button hands to the clipboard.
/// Composition lives here rather than in <c>Core/Reporting</c> because it needs localized labels,
/// and <c>Core</c> carries no UI reference — see <c>MatchSummaryReport</c>. Both
/// <c>MatchResult</c> and <c>FormationOverview</c> call this, so the layout is spelled out once.
/// </summary>
public static class MatchSummaryTextBuilder
{
    /// <summary>A plain-character stand-in for the live timeline's half-time rule, without its
    /// "Half time" label — the paste is a message, not a report.</summary>
    private const string HalfBreak = "———————————";

    public static string Build(Game game, MatchSummary summary, IStringLocalizer<Strings> L)
    {
        var homeName = game.IsHomeGame ? L["Us"].Value : game.Opponent;
        var awayName = game.IsHomeGame ? game.Opponent : L["Us"].Value;

        var lines = new List<string>
        {
            $"📆{game.DateLine("dd MMMM yyyy")}",
            "",
            $"{homeName} {summary.Score} {awayName}"
        };

        if (summary.Goals.Count > 0)
        {
            lines.Add("");
            lines.AddRange(GoalLines(summary.Goals));
        }

        if (summary.PublicComments.Count > 0)
        {
            lines.Add("");
            lines.AddRange(summary.PublicComments);
        }

        return string.Join('\n', lines);
    }

    // A break only where two consecutive goals cross half time — with no goal on one side of it,
    // nothing is drawn.
    private static IEnumerable<string> GoalLines(IReadOnlyList<MatchSummaryGoal> goals)
    {
        for (var i = 0; i < goals.Count; i++)
        {
            if (i > 0 && goals[i - 1].Half != goals[i].Half)
                yield return HalfBreak;

            yield return GoalLine(goals[i]);
        }
    }

    /// <summary>⚽ and 🅰️ carry the meaning a WhatsApp paste needs, without spending a line on
    /// each — see the issue: emoji survive a paste where any formatting would not.</summary>
    private static string GoalLine(MatchSummaryGoal goal)
    {
        var minutePart = goal.Minute is { } minute ? $" ({minute}')" : "";
        var assistPart = goal.AssistName is { } assist ? $" 🅰️ {assist}" : "";
        return $"⚽ {goal.ScorerName}{minutePart}{assistPart}";
    }
}
