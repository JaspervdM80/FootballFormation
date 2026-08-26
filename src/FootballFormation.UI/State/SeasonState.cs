namespace FootballFormation.UI.State;

/// A cookie and never <see cref="Season.IsCurrent"/>: the picker is a view choice an anonymous visitor can make, and that flag is shared
/// admin-owned state. Read-only here too, because choosing a season is a navigation to /season/set rather than a call on this class.
public class SeasonState(SeasonService seasons, RequestContext request)
{
    private Task? _loading;

    /// Both scopes a page renders in have a request to read this off, so neither pass has to ask the browser and the two cannot disagree.
    private readonly StoredSeason? _restored = SeasonPreference.Parse(request.SeasonCookie);

    public List<Season> Seasons { get; private set; } = [];

    /// Null means "all seasons" — no filter.
    public int? SelectedSeasonId { get; private set; }

    public Season? SelectedSeason => Seasons.FirstOrDefault(s => s.Id == SelectedSeasonId);

    /// MainLayout and the page both need this during their own OnInitializedAsync and interleave at the first await, so the first caller
    /// runs the query and the rest await that same task. An optimisation now, not a correctness requirement.
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var result = await seasons.GetAllAsync();

        // Swallowed on purpose: SeasonService already logged it, and a non-UI service has no snackbar. The picker then renders nothing.
        if (result.IsFailure) return;

        Seasons = result.Value!;

        // The remembered choice only wins while it still names a season that exists, or one deleted since would filter every page down
        // to nothing.
        SelectedSeasonId = _restored is not null && IsSelectable(_restored.SeasonId)
            ? _restored.SeasonId
            : Seasons.FirstOrDefault(s => s.IsCurrent)?.Id ?? Seasons.FirstOrDefault()?.Id;
    }

    private bool IsSelectable(int? seasonId) =>
        seasonId is null || Seasons.Any(s => s.Id == seasonId);

    /// Re-reads the list after /settings changes a season, so the page showing that list sees its own edit.
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
