namespace FootballFormation.UI.State;

/// A cookie because every deploy drops every open circuit, and everyone watching a match would otherwise come back to whichever season
/// the database calls current. Eight hours is a match day, not a subscription.
public static class SeasonPreference
{
    public const string CookieName = "ff.season";

    /// "All seasons" needs a value of its own, because null already spells it and would be indistinguishable from nothing stored at all.
    public const string AllSeasons = "all";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    /// An absent cookie and a hand-edited one are the same thing here, neither worth an error.
    public static StoredSeason? Parse(string? cookieValue) => cookieValue switch
    {
        null or "" => null,
        AllSeasons => new StoredSeason(null),
        _ => int.TryParse(cookieValue, out var id) ? new StoredSeason(id) : null,
    };

    /// How a choice is spelled in the cookie and in the /season/set query string.
    public static string Format(int? seasonId) => seasonId?.ToString() ?? AllSeasons;
}

/// A null <paramref name="SeasonId"/> means "all seasons".
public sealed record StoredSeason(int? SeasonId);
