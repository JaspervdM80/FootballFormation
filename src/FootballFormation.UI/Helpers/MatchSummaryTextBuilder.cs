using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Helpers;

/// Here rather than in Core/Reporting because it needs localized labels and Core carries no UI reference. Both MatchResult and
/// FormationOverview call it, so the layout is spelled out once.
public static class MatchSummaryTextBuilder
{
    /// A plain-character stand-in for the live timeline's half-time rule: the paste is a message, not a report.
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

    // A break only where two consecutive goals cross half time — with no goal on one side of it, nothing is drawn.
    private static IEnumerable<string> GoalLines(IReadOnlyList<MatchSummaryGoal> goals)
    {
        for (var i = 0; i < goals.Count; i++)
        {
            if (i > 0 && goals[i - 1].Half != goals[i].Half)
                yield return HalfBreak;

            yield return GoalLine(goals[i]);
        }
    }

    /// Emoji survive a paste where any formatting would not, and carry the meaning without spending a line on each.
    private static string GoalLine(MatchSummaryGoal goal)
    {
        var minutePart = goal.Minute is { } minute ? $" ({minute}')" : "";
        var assistPart = goal.AssistName is { } assist ? $" 🅰️ {assist}" : "";
        return $"⚽ {goal.ScorerName}{minutePart}{assistPart}";
    }
}
