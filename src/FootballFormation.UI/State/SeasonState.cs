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
/// <para>
/// Read-only from the app's side: choosing a season is a navigation to <c>/season/set</c>, not a
/// call on this class. The layout renders statically for every page, so the picker has no circuit
/// to raise an event in — and with the choice arriving on the next request, there is nothing left
/// for a change notification to tell anybody.
/// </para>
/// </summary>
public class SeasonState(SeasonService seasons, RequestContext request)
{
    private Task? _loading;

    /// <summary>
    /// The remembered choice, off the request that created this scope. Both scopes a page is
    /// rendered in have one — the static render has the page request, and a circuit is created
    /// during the <c>/_blazor</c> request, which carries the same cookies — so neither pass has to
    /// ask the browser and the two cannot disagree.
    /// </summary>
    private readonly StoredSeason? _restored = SeasonPreference.Parse(request.SeasonCookie);

    public List<Season> Seasons { get; private set; } = [];

    /// <summary>The season being filtered by. Null means "all seasons" — no filter.</summary>
    public int? SelectedSeasonId { get; private set; }

    public Season? SelectedSeason => Seasons.FirstOrDefault(s => s.Id == SelectedSeasonId);

    /// <summary>
    /// Loads the season list once per scope. MainLayout and the page both need it during their
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

    /// <summary>Re-reads the list after /settings adds, edits or removes a season, so the page
    /// showing that list sees its own edit.</summary>
    public async Task RefreshAsync()
    {
        var previous = SelectedSeasonId;

        _loading = null;
        await EnsureLoadedAsync();

        // Keep the viewer where they were, unless that season is gone.
        if (previous is not null && Seasons.Any(s => s.Id == previous))
            SelectedSeasonId = previous;
    }
}
