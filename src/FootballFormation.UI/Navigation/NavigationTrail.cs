using FootballFormation.UI.State;

namespace FootballFormation.UI.Navigation;

/// Read off the <c>Referer</c> header, which a circuit's own scope has none of (/_blazor) — so a back button inside an interactive
/// island falls through to its <c>Fallback</c>.
public sealed class NavigationTrail(NavigationManager navigation, RequestContext request)
{
    /// Null when there is nothing usable behind: a shared link opened cold, a bookmark, or a referrer from somewhere else entirely.
    public string? Previous
    {
        get
        {
            if (request.Referer is not { Length: > 0 } referer) return null;

            // Same origin, and only a path this app can name: the arrow must never offer to leave the site or to go somewhere unlabelled.
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return null;
            if (!navigation.BaseUri.StartsWith($"{uri.Scheme}://{uri.Authority}/", StringComparison.OrdinalIgnoreCase))
                return null;

            var path = uri.PathAndQuery;
            return path == "/" + navigation.ToBaseRelativePath(navigation.Uri) ? null : path;
        }
    }

    /// Replaces the history entry rather than stacking on it, or both this app's back arrow and the browser's would point at the page
    /// that just failed and bounce the visitor straight into it.
    public void Redirect(string path) => navigation.NavigateTo(path, replace: true);
}
