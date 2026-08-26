using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Navigation;

/// <summary>
/// Where the visitor came from, so a back button can return them there instead of to whichever page
/// someone hardcoded. Reaching a player from the season statistics and reaching the same player from
/// the squad used to end up in the same place; now each goes back where it came from.
/// Read off the <c>Referer</c> header, which a circuit's own scope has none of (/_blazor), so a back button inside an interactive island falls through to its <c>Fallback</c>.
/// </summary>
public sealed class NavigationTrail(NavigationManager navigation, RequestContext request)
{
    /// <summary>
    /// The page before the current one, or null when there is nothing usable behind: a shared link
    /// opened cold, a bookmark, or a referrer from somewhere else entirely.
    /// </summary>
    public string? Previous
    {
        get
        {
            if (request.Referer is not { Length: > 0 } referer) return null;

            // Same origin only, and only a path this app can name — the back arrow does not offer
            // to return to a page it cannot label, and must never offer to leave the site.
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return null;
            if (!navigation.BaseUri.StartsWith($"{uri.Scheme}://{uri.Authority}/", StringComparison.OrdinalIgnoreCase))
                return null;

            var path = uri.PathAndQuery;
            return path == "/" + navigation.ToBaseRelativePath(navigation.Uri) ? null : path;
        }
    }

    /// <summary>
    /// Navigate away from a page that failed to load, replacing it rather than stacking on top of
    /// it. Without this the redirect target's back button would point at the page that just failed
    /// and bounce the visitor straight into it — and so would the browser's own back button, which
    /// is why the history entry is replaced too.
    /// </summary>
    public void Redirect(string path) => navigation.NavigateTo(path, replace: true);
}
