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
    public static string Build(Game game, MatchSummary summary, IStringLocalizer<Strings> L)
    {
        var homeName = game.IsHomeGame ? L["Us"].Value : game.Opponent;
        var awayName = game.IsHomeGame ? game.Opponent : L["Us"].Value;

        var lines = new List<string>
        {
            $"{homeName} {summary.Score} {awayName}",
            Subtitle(game, L)
        };

        // Reuses the live screen's own key rather than a second one for the same word — see
        // Strings.nl.resx and the localization skill on duplicate resx keys.
        if (summary.HalfTimeScore is { } ht)
            lines.Add($"{L["Half time"]}: {ht}");

        if (summary.Goals.Count > 0)
        {
            lines.Add("");
            lines.AddRange(summary.Goals.Select(g => GoalLine(g, L)));
        }

        if (summary.PublicComments.Count > 0)
        {
            lines.Add("");
            lines.AddRange(summary.PublicComments);
        }

        return string.Join('\n', lines);
    }

    private static string Subtitle(Game game, IStringLocalizer<Strings> L) =>
        $"{L[game.MatchType.DisplayName()]} · {game.DateLine("dd MMMM yyyy")}";

    /// <summary>⚽ and 🅰️ carry the meaning a WhatsApp paste needs, without spending a line on
    /// each — see the issue: emoji survive a paste where any formatting would not.</summary>
    private static string GoalLine(MatchSummaryGoal goal, IStringLocalizer<Strings> L)
    {
        var minutePart = goal.Minute is { } minute ? $" ({minute}')" : "";
        var assistPart = goal.AssistName is { } assist ? $" 🅰️ {assist}" : "";
        return $"⚽ {goal.ScorerName}{minutePart}{assistPart}";
    }
}
