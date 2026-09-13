namespace FootballFormation.Core.Models;

/// How many a side the match is played. Never stored: a shape fields as many as it fields, so the format is read back off the formation
/// the same way PeriodCount is read off the period table.
public enum MatchFormat
{
    NineASide = 9,
    ElevenASide = 11
}

public static class MatchFormatExtensions
{
    /// Largest first, so the picker opens on the full-size game.
    public static IReadOnlyList<MatchFormat> Descending { get; } =
        [.. Enum.GetValues<MatchFormat>().OrderByDescending(format => (int)format)];

    public static int PlayerCount(this MatchFormat format) => (int)format;

    public static string DisplayName(this MatchFormat format) => format switch
    {
        MatchFormat.ElevenASide => "11 vs 11",
        MatchFormat.NineASide => "9 vs 9",
        _ => format.ToString()
    };

    /// What a format opens on when it is picked, before the coach chooses a shape of her own.
    public static FormationType DefaultFormation(this MatchFormat format) => format switch
    {
        MatchFormat.NineASide => FormationType.F332,
        _ => FormationType.F442
    };
}
