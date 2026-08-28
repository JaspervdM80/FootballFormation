using FootballFormation.UI.State;

namespace FootballFormation.UI.Navigation;

/// Read off the trail cookie the host writes for every page it serves, which a circuit's own scope has as well — see
/// <see cref="NavigationTrailCookie"/> for why the <c>Referer</c> header cannot answer this.
public sealed class NavigationTrail(NavigationManager navigation, RequestContext request)
{
    /// Null when there is nothing usable behind: a shared link opened cold, a bookmark, or a trail of pages this app cannot name. The
    /// page we are on is skipped rather than returned — the cookie a circuit reads was written by that page's own response.
    public string? Previous
    {
        get
        {
            var current = "/" + navigation.ToBaseRelativePath(navigation.Uri);

            return NavigationTrailCookie.Parse(request.TrailCookie)
                .FirstOrDefault(path => path != current && AppNav.PageNameKey(path) is not null);
        }
    }

    /// Replaces the history entry rather than stacking on it, or both this app's back arrow and the browser's would point at the page
    /// that just failed and bounce the visitor straight into it.
    public void Redirect(string path) => navigation.NavigateTo(path, replace: true);
}
