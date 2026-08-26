namespace FootballFormation.Core.Models;

/// Descriptive only — every type counts towards the season table and player statistics alike, so no report branches on it. Adding a type
/// needs a Dutch entry in Strings.nl.resx; never renumber an existing one, the numbers are in the database.
public enum MatchType
{
    Competition = 0,
    Cup = 1,
    Practice = 2
}

public static class MatchTypeExtensions
{
    /// Rendered as <c>@L[game.MatchType.DisplayName()]</c>, like <c>PlayerPosition</c>.
    public static string DisplayName(this MatchType type) => type switch
    {
        MatchType.Competition => "Competition",
        MatchType.Cup => "Cup",
        MatchType.Practice => "Practice",
        _ => type.ToString()
    };
}
