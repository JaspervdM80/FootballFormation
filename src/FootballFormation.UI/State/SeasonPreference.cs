namespace FootballFormation.UI.State;

/// <summary>
/// The season picker's choice, kept in a cookie so it survives a reload.
/// <para>
/// That reload is not hypothetical: the app is one Fly machine, and every deploy — which is every
/// merge to main — drops every open circuit. Before this, everyone watching a match came back to
/// whichever season the database calls current.
/// </para>
/// <para>
/// Eight hours is a match day, not a subscription. Long enough that a Saturday spent in last
/// season's numbers stays there through however many restarts, short enough that it has forgotten
/// by the next time anyone opens the app.
/// </para>
/// <para>
/// Both halves now go through the request. Reading: every scope a page renders in was created by a
/// request already carrying the cookie, so it arrives through <see cref="RequestContext"/>.
/// Writing: the picker is a link to <c>/season/set</c>, which has a response to put a
/// <c>Set-Cookie</c> on — where the picker used to be a handler in a circuit that had none, and had
/// to ask the browser to write the cookie for it.
/// </para>
/// </summary>
public static class SeasonPreference
{
    public const string CookieName = "ff.season";

    /// <summary>"All seasons" is a choice, and null is already how it is spelled — so it needs a
    /// value of its own in the cookie to be distinguishable from nothing stored at all.</summary>
    public const string AllSeasons = "all";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    /// <summary>Reads the cookie's raw value. Null when there is nothing usable to restore — an
    /// absent cookie and a hand-edited one are the same thing here, neither worth an error.</summary>
    public static StoredSeason? Parse(string? cookieValue) => cookieValue switch
    {
        null or "" => null,
        AllSeasons => new StoredSeason(null),
        _ => int.TryParse(cookieValue, out var id) ? new StoredSeason(id) : null,
    };

    /// <summary>How a choice is spelled in the cookie and in the /season/set query string.</summary>
    public static string Format(int? seasonId) => seasonId?.ToString() ?? AllSeasons;
}

/// <summary>A restored choice. <see cref="SeasonId"/> null means "all seasons".</summary>
/// <param name="SeasonId">The season to filter by, or null for no filter.</param>
public sealed record StoredSeason(int? SeasonId);
