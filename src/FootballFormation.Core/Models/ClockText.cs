using System.Globalization;

namespace FootballFormation.Core.Models;

/// How a time of day is written and read here: 24 hours, and never the browser's idea of it — see
/// docs/ui_components/live-match-screen.md for why a native time input could not be asked.
public static class ClockText
{
    public const string Format = "HH:mm";

    /// What the fields show as a placeholder, so the example and the format cannot drift apart.
    public const string Example = "13:45";

    /// "9:30" is a time and not a typo; it is written back in <see cref="Format"/> either way.
    private static readonly string[] Accepted = [Format, "H:mm"];

    public static string? Of(TimeSpan? time) => time?.ToString(@"hh\:mm");

    /// Null for anything that is not a time of day. <see cref="TimeOnly"/> rather than <see cref="TimeSpan"/>, which would read 25:00 as
    /// a duration.
    public static TimeSpan? Parse(string? text) =>
        TimeOnly.TryParseExact(text, Accepted, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToTimeSpan()
            : null;

    /// Takes what a thumb actually types on a numeric keypad — 1045 and 930 as readily as 10:45 — and settles it on <see cref="Format"/>.
    /// Text it cannot read comes back untouched, for the caller's validation to report rather than the field rewriting it.
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var typed = text.Trim();
        return Parse(WithSeparator(typed)) is { } time ? Of(time) : typed;
    }

    /// Only a run of bare digits is reshaped. A half-typed "10:4" already carries its separator, and reading a shape off its digits alone
    /// would settle it on 01:04 — a different time, quietly, where the reader wanted the error.
    private static string WithSeparator(string typed) =>
        !typed.All(char.IsAsciiDigit) ? typed : typed.Length switch
        {
            4 => $"{typed[..2]}:{typed[2..]}",
            3 => $"0{typed[0]}:{typed[1..]}",
            _ => typed
        };
}
