namespace FootballFormation.Core.Models;

/// <summary>
/// What kind of fixture a game is. Descriptive only — every type counts towards the season table
/// and player statistics alike, so nothing in <c>SeasonStatsReport</c> or <c>PlayerStatsReport</c>
/// branches on it.
/// <para>
/// <see cref="Competition"/> is 0 so it is what every pre-existing game reads as, and what a new
/// game defaults to. Adding a type is a new member plus a Dutch entry in Strings.nl.resx — never
/// renumber an existing one, the numbers are in the database.
/// </para>
/// </summary>
public enum MatchType
{
    Competition = 0,
    Cup = 1,
    Practice = 2
}

public static class MatchTypeExtensions
{
    /// <summary>
    /// The English name, which doubles as the resource key — callers render it as
    /// <c>@L[game.MatchType.DisplayName()]</c>, the same convention as <c>PlayerPosition</c>.
    /// </summary>
    public static string DisplayName(this MatchType type) => type switch
    {
        MatchType.Competition => "Competition",
        MatchType.Cup => "Cup",
        MatchType.Practice => "Practice",
        _ => type.ToString()
    };
}
