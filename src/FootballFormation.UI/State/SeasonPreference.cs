using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

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
/// Writing needs the browser, because an established circuit has no response to set a cookie on.
/// <b>Reading does not</b>: every scope a page renders in was created by a request already carrying
/// the cookie, so it arrives through <see cref="RequestContext"/>. Asking the browser for it
/// instead would put a round trip in front of the first interactive render, and would leave the
/// static pass painting a season the cookie had already overruled.
/// </para>
/// </summary>
public class SeasonPreference(IJSRuntime js, ILogger<SeasonPreference> log)
{
    public const string CookieName = "ff.season";

    /// <summary>"All seasons" is a choice, and null is already how it is spelled — so it needs a
    /// value of its own in the cookie to be distinguishable from nothing stored at all.</summary>
    private const string AllSeasons = "all";

    private const int LifetimeSeconds = 8 * 60 * 60;

    /// <summary>Reads the cookie's raw value. Null when there is nothing usable to restore — an
    /// absent cookie and a hand-edited one are the same thing here, neither worth an error.</summary>
    public static StoredSeason? Parse(string? cookieValue) => cookieValue switch
    {
        null or "" => null,
        AllSeasons => new StoredSeason(null),
        _ => int.TryParse(cookieValue, out var id) ? new StoredSeason(id) : null,
    };

    public async Task SaveAsync(int? seasonId)
    {
        try
        {
            await js.InvokeVoidAsync(
                "seasonCookie.set", seasonId?.ToString() ?? AllSeasons, LifetimeSeconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException
                                      or OperationCanceledException)
        {
            // No browser to write to: the prerender pass, which runs before a circuit exists, or a
            // circuit already on its way out. Neither is a failure — the choice simply is not
            // remembered, and the picker still shows it for as long as this circuit lives.
        }
        catch (JSException ex)
        {
            // The call reached the browser and the browser refused it: js/season.js missing from
            // the page, renamed, or 404'd behind a stale service worker. That is a deployment
            // fault, not a lifecycle one, and it is invisible from the UI — the picker keeps
            // working and only forgetting is broken. Say so somewhere.
            log.LogWarning(ex, "Could not store the season preference; is js/season.js loaded?");
        }
    }
}

/// <summary>A restored choice. <see cref="SeasonId"/> null means "all seasons".</summary>
/// <param name="SeasonId">The season to filter by, or null for no filter.</param>
public sealed record StoredSeason(int? SeasonId);
