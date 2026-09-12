using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Helpers;

/// Title and body for one push, localized. Here rather than in Core/Reporting for the same reason MatchSummaryTextBuilder is: Core
/// carries no UI reference. A service worker cannot reach IStringLocalizer at all, so this runs on the server and the finished words
/// travel in the payload.
public static class MatchNotificationTextBuilder
{
    public static (string Title, string Body) Build(MatchNotification notification, string teamName, IStringLocalizer<Strings> L)
    {
        var homeName = notification.IsHomeGame ? teamName : notification.Opponent;
        var awayName = notification.IsHomeGame ? notification.Opponent : teamName;
        var scoreline = $"{homeName} {notification.Score} {awayName}";

        return notification.Event switch
        {
            LiveMatchEvent.KickOff => (L["The match has started"], $"{homeName} - {awayName}"),
            LiveMatchEvent.Goal => (GoalTitle(notification, L), scoreline),
            LiveMatchEvent.FullTime => (L["Full time"], scoreline),
            _ => (teamName, scoreline)
        };
    }

    private static string GoalTitle(MatchNotification notification, IStringLocalizer<Strings> L)
    {
        var minutePart = notification.Minute is { } minute ? $" ({minute}')" : "";

        return notification.ScorerName is { } scorer
            ? $"⚽ {scorer}{minutePart}"
            : $"{L["Goal against"]}{minutePart}";
    }
}
