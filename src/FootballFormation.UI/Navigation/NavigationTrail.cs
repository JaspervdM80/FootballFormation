using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace FootballFormation.UI.Navigation;

/// <summary>
/// Where the visitor has been in this circuit, so a back button can return them there instead of to
/// whichever page someone hardcoded. Reaching a player from the season stats and reaching the same
/// player from the squad used to end up in the same place; now each goes back where it came from.
/// <para>
/// Scoped, so on Blazor Server this lives for the SignalR circuit: the trail survives navigation
/// within a tab and resets on a browser refresh. That reset is why the back arrow carries a
/// fallback — a shared link opened cold has nothing behind it.
/// </para>
/// </summary>
public sealed class NavigationTrail(NavigationManager navigation) : IDisposable
{
    // A visitor twenty pages deep does not need the twenty-first remembered, and an unbounded list
    // on a circuit that can stay open for hours is a slow leak.
    private const int MaxDepth = 20;

    private readonly List<string> _trail = [];
    private bool _started;
    private bool _replacingCurrent;

    /// <summary>
    /// Starts recording. Called once by MainLayout, which is on screen before any page renders.
    /// <para>
    /// Doing this here rather than in the constructor is the whole trick: a scoped service is not
    /// built until something injects it, and the first injector would otherwise be a detail page's
    /// back button — by which time the navigation that led there has already happened unobserved,
    /// which is the exact bug this class exists to fix. LocationChanged also never fires for the
    /// page a circuit starts on, so that first entry is seeded by hand.
    /// </para>
    /// </summary>
    public void Start()
    {
        if (_started) return;

        _started = true;
        Record(ToPath(navigation.Uri));
        navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// The page before the current one, or null when this is where the visit began.
    /// <para>
    /// Records the current URL before answering. The Router subscribed to LocationChanged before we
    /// did and re-renders the page inside its own handler, so a component can read this before our
    /// handler has run. <see cref="Record"/> is idempotent, so doing it from both ends is safe.
    /// </para>
    /// </summary>
    public string? Previous
    {
        get
        {
            Record(ToPath(navigation.Uri));
            return _trail.Count >= 2 ? _trail[^2] : null;
        }
    }

    /// <summary>
    /// Navigate away from a page that failed to load, replacing it rather than stacking on top of
    /// it. Without this the redirect target's back button would point at the page that just failed
    /// and bounce the visitor straight into it — and so would the browser's own back button, which
    /// is why the history entry is replaced too.
    /// </summary>
    public void Redirect(string path)
    {
        _replacingCurrent = true;
        navigation.NavigateTo(path, replace: true);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        Record(ToPath(e.Location));

    private void Record(string path)
    {
        var replacing = _replacingCurrent;
        _replacingCurrent = false;

        if (_trail.Count > 0 && _trail[^1] == path) return;

        // Already behind us — the browser's own back/forward buttons, or our own back arrow. Unwind
        // to it rather than pushing a second copy, or the two ping-pong forever. The cost is that a
        // genuine forward link to a page already visited collapses the trail between the two; no
        // path through this app's links does that.
        var seen = _trail.LastIndexOf(path);
        if (seen >= 0)
        {
            _trail.RemoveRange(seen + 1, _trail.Count - seen - 1);
            return;
        }

        if (replacing && _trail.Count > 0) _trail.RemoveAt(_trail.Count - 1);

        _trail.Add(path);
        if (_trail.Count > MaxDepth) _trail.RemoveAt(0);
    }

    // Stored with a leading slash so entries compare equal to the AppRoutes constants and can be
    // handed straight back to NavigateTo. Query strings are kept on purpose: returning to a
    // filtered list should return to the filter too.
    private string ToPath(string uri) => "/" + navigation.ToBaseRelativePath(uri);

    // Unsubscribing an unsubscribed handler is a no-op, so this is safe even if Start never ran.
    public void Dispose() => navigation.LocationChanged -= OnLocationChanged;
}
