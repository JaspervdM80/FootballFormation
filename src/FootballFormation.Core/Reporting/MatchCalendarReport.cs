using System.Globalization;
using System.Text;

namespace FootballFormation.Core.Reporting;

/// The fixtures as an iCalendar (RFC 5545) document, for a one-off download and for the feed a calendar app subscribes to. The
/// description comes in as text because its labels are localized, which Core cannot do.
public static class MatchCalendarReport
{
    public const string TimeZoneId = "Europe/Amsterdam";

    private static readonly TimeSpan HalfTime = TimeSpan.FromMinutes(15);

    public static string Build(
        string ourTeamName,
        IEnumerable<Game> games,
        Func<Game, string> describe,
        Func<Game, string> link,
        DateTime utcNow)
    {
        var ics = new StringBuilder();

        Line(ics, "BEGIN:VCALENDAR");
        Line(ics, "VERSION:2.0");
        Line(ics, "PRODID:-//FootballFormation//Matches//NL");
        Line(ics, "CALSCALE:GREGORIAN");
        Line(ics, "METHOD:PUBLISH");
        Line(ics, $"X-WR-CALNAME:{Escape(ourTeamName)}");
        Line(ics, $"X-WR-TIMEZONE:{TimeZoneId}");
        // Hints only: Apple and Outlook honour them, Google polls on its own schedule whatever this says.
        Line(ics, "REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Line(ics, "X-PUBLISHED-TTL:PT1H");
        AppendTimeZone(ics);

        foreach (var game in games.OldestFirst())
            AppendEvent(ics, game, ourTeamName, describe(game), link(game), utcNow);

        Line(ics, "END:VCALENDAR");
        return ics.ToString();
    }

    /// Stable per game, so a refresh or a second download replaces the event rather than adding a duplicate beside it.
    public static string UidFor(Game game) => $"game-{game.Id}@footballformation";

    /// Null when the game has neither a meet time nor a kick-off, which is an all-day event.
    public static (DateTime Start, DateTime End)? TimesOf(Game game)
    {
        DateTime? kickOff = game.HasStartTime ? game.Date : null;
        DateTime? meet = game.MeetTime is { } time ? game.Date.Date + time : null;

        var start = (meet, kickOff) switch
        {
            ({ } m, { } k) => m < k ? m : k,
            ({ } m, null) => m,
            (null, { } k) => k,
            _ => (DateTime?)null
        };

        if (start is null) return null;

        var end = (kickOff ?? start.Value) + TimeSpan.FromMinutes(game.GameDurationMinutes) + HalfTime;
        return (start.Value, end);
    }

    public static string SummaryOf(Game game, string ourTeamName)
    {
        var home = game.IsHomeGame ? ourTeamName : game.Opponent;
        var away = game.IsHomeGame ? game.Opponent : ourTeamName;

        if (!game.HasFinalScore) return $"{home} – {away}";

        var score = game.ScoreboardOrder();
        return $"{home} {score.Home}-{score.Away} {away}";
    }

    /// Only what a maps app can find: a bare field number means nothing there, and the description already names the field.
    public static string? LocationOf(Game game)
    {
        var parts = new[] { game.SportsPark, game.City }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static void AppendEvent(StringBuilder ics, Game game, string ourTeamName, string description, string url, DateTime utcNow)
    {
        Line(ics, "BEGIN:VEVENT");
        Line(ics, $"UID:{UidFor(game)}");
        Line(ics, $"DTSTAMP:{utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");

        if (TimesOf(game) is var (start, end))
        {
            Line(ics, $"DTSTART;TZID={TimeZoneId}:{Local(start)}");
            Line(ics, $"DTEND;TZID={TimeZoneId}:{Local(end)}");
        }
        else
        {
            Line(ics, $"DTSTART;VALUE=DATE:{Day(game.Date)}");
            Line(ics, $"DTEND;VALUE=DATE:{Day(game.Date.AddDays(1))}");
        }

        Line(ics, $"SUMMARY:{Escape(SummaryOf(game, ourTeamName))}");
        if (LocationOf(game) is { } location) Line(ics, $"LOCATION:{Escape(location)}");
        Line(ics, $"DESCRIPTION:{Escape(description)}");
        Line(ics, $"URL:{url}");
        Line(ics, "END:VEVENT");
    }

    /// Fixed rather than read from the host's time zone database, which a slim container image may not carry.
    private static void AppendTimeZone(StringBuilder ics)
    {
        Line(ics, "BEGIN:VTIMEZONE");
        Line(ics, $"TZID:{TimeZoneId}");
        Line(ics, "BEGIN:DAYLIGHT");
        Line(ics, "TZOFFSETFROM:+0100");
        Line(ics, "TZOFFSETTO:+0200");
        Line(ics, "TZNAME:CEST");
        Line(ics, "DTSTART:19700329T020000");
        Line(ics, "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU");
        Line(ics, "END:DAYLIGHT");
        Line(ics, "BEGIN:STANDARD");
        Line(ics, "TZOFFSETFROM:+0200");
        Line(ics, "TZOFFSETTO:+0100");
        Line(ics, "TZNAME:CET");
        Line(ics, "DTSTART:19701025T030000");
        Line(ics, "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU");
        Line(ics, "END:STANDARD");
        Line(ics, "END:VTIMEZONE");
    }

    private static string Local(DateTime value) => value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    private static string Day(DateTime value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "\\n");

    /// Folded at 75 octets, not characters: the description carries emoji, and a fold may not split a UTF-8 sequence.
    private static void Line(StringBuilder ics, string content)
    {
        const int limit = 75;
        var octets = 0;

        foreach (var rune in content.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                ics.Append("\r\n ");
                octets = 1;
            }

            ics.Append(rune.ToString());
            octets += size;
        }

        ics.Append("\r\n");
    }
}
