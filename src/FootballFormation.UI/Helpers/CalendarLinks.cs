using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Helpers;

/// Links off the site: the team's match calendar as a subscription rather than a download, and a map search for an away ground.
public static class CalendarLinks
{
    /// webcal:// is what makes iOS, macOS and desktop Outlook subscribe to the feed instead of importing a one-off copy of it.
    public static string Subscribe(string baseUri, int teamId)
    {
        var feed = new Uri(new Uri(baseUri), AppRoutes.TeamCalendar(teamId));
        return $"webcal://{feed.Authority}{feed.PathAndQuery}";
    }

    /// Android has no webcal handler, so a phone there subscribes through Google Calendar's own page.
    public static string SubscribeInGoogle(string baseUri, int teamId) =>
        $"https://calendar.google.com/calendar/render?cid={Uri.EscapeDataString(Subscribe(baseUri, teamId))}";

    /// The same place the calendar event names, so the two cannot disagree. Null when there is nothing to search for.
    public static string? Route(Game game) =>
        MatchCalendarReport.LocationOf(game) is { } place
            ? $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(place)}"
            : null;
}
