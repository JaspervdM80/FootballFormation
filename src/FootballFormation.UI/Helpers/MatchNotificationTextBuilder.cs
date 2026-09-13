using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Helpers;

/// Title and body for one push, localized. Here rather than in Core/Reporting for the same reason MatchSummaryTextBuilder is: Core
/// carries no UI reference. A service worker cannot reach IStringLocalizer at all, so this runs on the server and the finished words
/// travel in the payload.
public static class MatchNotificationTextBuilder
{
    /// The sides are already named and in venue order — see MatchNotification, which is where that is decided and tested.
    public static (string Title, string Body) Build(MatchNotification notification, IStringLocalizer<Strings> L)
    {
        var scoreline = $"{notification.HomeName} {notification.Score} {notification.AwayName}";

        // Exhaustive on purpose: a fourth kind wired through later must not inherit the goal wording and announce half time as a goal.
        return notification.Event switch
        {
            LiveMatchEvent.KickOff => (L["Match started"], $"{notification.HomeName} - {notification.AwayName}"),
            LiveMatchEvent.Goal => (GoalTitle(notification, L), scoreline),
            LiveMatchEvent.FullTime => (L["Full time"], scoreline),
            _ => (notification.HomeName, scoreline)
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
