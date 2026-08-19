using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using Microsoft.Extensions.Localization;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Turns a <see cref="MatchSummary"/> into the plain text a copy button hands to the clipboard.
/// Composition lives here rather than in <c>Core/Reporting</c> because it needs localized labels,
/// and <c>Core</c> carries no UI reference — see <c>MatchSummaryReport</c>. Both
/// <c>MatchResult</c> and <c>FormationOverview</c> call this, so the layout is spelled out once.
/// </summary>
public static class MatchSummaryTextBuilder
{
    /// <summary>The same dashed break the live timeline draws between halves — see
    /// <c>LiveMatch.razor.css</c> — spelled out in plain characters since a paste target has no CSS
    /// to draw a rule with.</summary>
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

    /// <summary>One line per goal, with the half break inserted wherever two consecutive goals
    /// cross it — the same transition <c>LiveMatch.razor.cs</c>'s <c>HalfTimeAbove</c> tests for,
    /// so a break with no goal on one side of it stays silent here too.</summary>
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
