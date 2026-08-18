using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Navigation;

/// <summary>
/// Where the visitor came from, so a back button can return them there instead of to whichever page
/// someone hardcoded. Reaching a player from the season statistics and reaching the same player from
/// the squad used to end up in the same place; now each goes back where it came from.
/// <para>
/// This reads the <c>Referer</c> header off the request rather than keeping a list of navigations,
/// and the reason is the render-mode split. The list only ever existed inside a circuit, and
/// <c>MainLayout</c> — its only entry point — is statically rendered on every page now. The browser
/// already knows the answer and sends it on the request, so the trail was reconstructing something
/// it was being told. It survives a refresh as a side effect, which the list never did.
/// </para>
/// <para>
/// A circuit's own scope is the exception, and deliberately so: it is created during the
/// <c>/_blazor</c> request, which carries no referrer. A back button inside an interactive island
/// therefore falls through to its <c>Fallback</c> — and each of those pages (the builder, the live
/// screen, the match result) is reached from /games, which is what its fallback already says.
/// </para>
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
    /// Navigate away from a page that failed to load, replacing the history entry rather than
    /// stacking on it, so the browser's own back button does not bounce the visitor straight into
    /// the page that just failed.
    /// <para>
    /// Moot during a static render, where <c>NavigateTo</c> becomes a real redirect and adds no
    /// history entry of its own — which is most of the time now. It stays because the pages that
    /// call it are reached in both modes.
    /// </para>
    /// </summary>
    public void Redirect(string path) => navigation.NavigateTo(path, replace: true);
}
