namespace FootballFormation.UI.Navigation;

public sealed class NavigationTrail(NavigationManager navigation)
{
    /// Replaces the history entry rather than stacking on it, or both this app's back arrow and the browser's would point at the page
    /// that just failed and bounce the visitor straight into it.
    public void Redirect(string path) => navigation.NavigateTo(path, replace: true);
}
