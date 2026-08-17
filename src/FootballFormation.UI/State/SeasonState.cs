using FootballFormation.Core.Models;
using FootballFormation.Core.Services;

namespace FootballFormation.UI.State;

/// <summary>
/// The season the whole UI is filtered by. The choice is remembered in a cookie for eight hours
/// (see <see cref="SeasonPreference"/>) and otherwise falls back to the database's current season.
/// <para>
/// A cookie and not <see cref="Season.IsCurrent"/>. The picker is a <em>view</em> choice and must
/// never write that flag, which is shared, admin-owned state edited on /settings — anonymous
/// visitors can reach the picker. Per-browser is exactly the scope this belongs at.
/// </para>
/// </summary>
public class SeasonState(SeasonService seasons, SeasonPreference preference)
{
    private Task? _loading;
    private StoredSeason? _restored;

    public List<Season> Seasons { get; private set; } = [];

    /// <summary>The season being filtered by. Null means "all seasons" — no filter.</summary>
    public int? SelectedSeasonId { get; private set; }

    public Season? SelectedSeason => Seasons.FirstOrDefault(s => s.Id == SelectedSeasonId);

    public event Action? OnChanged;

    /// <summary>
    /// Hands over the season cookie from the request that is rendering the page. <c>Routes</c>
    /// calls this, which is what makes it work in both passes: the prerender reads the cookie off
    /// <c>HttpContext</c>, and the value travels to the circuit as a root-component parameter, so
    /// both passes resolve the same season and neither has to ask the browser.
    /// <para>
    /// Must land before <see cref="EnsureLoadedAsync"/>, and does: <c>Routes.OnInitialized</c> runs
    /// before the router renders the page that calls it.
    /// </para>
    /// </summary>
    public void Restore(string? cookieValue) => _restored = SeasonPreference.Parse(cookieValue);

    /// <summary>
    /// Loads the season list once per circuit. MainLayout and the page both need it during their
    /// own <c>OnInitializedAsync</c> and interleave at the first await, so the first caller runs
    /// the query and everyone else awaits that same task.
    /// <para>
    /// Memoizing is now purely an optimisation — a page that forgets it costs a duplicate query,
    /// not a crash. It was load-bearing while the services shared one scoped context and a second
    /// concurrent query threw.
    /// </para>
    /// </summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var result = await seasons.GetAllAsync();

        // Swallowed on purpose: a non-UI service has no snackbar and SeasonService already logged
        // it. The picker then renders nothing and the pages fall back to unfiltered.
        if (result.IsFailure) return;

        Seasons = result.Value!;

        // The remembered choice only wins while it still names a season that exists — a season
        // deleted since would otherwise filter every page down to nothing, with a picker that
        // cannot say which season it is showing.
        SelectedSeasonId = _restored is not null && IsSelectable(_restored.SeasonId)
            ? _restored.SeasonId
            : Seasons.FirstOrDefault(s => s.IsCurrent)?.Id ?? Seasons.FirstOrDefault()?.Id;
    }

    private bool IsSelectable(int? seasonId) =>
        seasonId is null || Seasons.Any(s => s.Id == seasonId);

    public async Task SelectAsync(int? seasonId)
    {
        if (SelectedSeasonId == seasonId) return;

        SelectedSeasonId = seasonId;

        // The choice this circuit is restored from, so a RefreshAsync after /settings edits the
        // season list falls back to what the viewer picked rather than to what they arrived with.
        _restored = new StoredSeason(seasonId);

        // Notify before persisting: the subscribers only queue a render, and nothing about the
        // page should wait on a round trip to the browser to store a cookie.
        OnChanged?.Invoke();
        await preference.SaveAsync(seasonId);
    }

    /// <summary>Re-reads the list after /settings adds, edits or removes a season, so the picker
    /// updates without a page reload.</summary>
    public async Task RefreshAsync()
    {
        var previous = SelectedSeasonId;

        _loading = null;
        await EnsureLoadedAsync();

        // Keep the viewer where they were, unless that season is gone.
        if (previous is not null && Seasons.Any(s => s.Id == previous))
            SelectedSeasonId = previous;

        OnChanged?.Invoke();
    }
}
