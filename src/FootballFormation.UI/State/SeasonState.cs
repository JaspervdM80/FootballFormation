using FootballFormation.Core.Models;
using FootballFormation.Core.Services;

namespace FootballFormation.UI.State;

/// <summary>
/// The season the whole UI is filtered by. Scoped, so on Blazor Server this lives for the SignalR
/// circuit: the choice survives navigation within a tab but resets on a browser refresh, where it
/// falls back to the database's current season.
/// <para>
/// That reset is deliberate. The picker is a <em>view</em> choice and must never write
/// <see cref="Season.IsCurrent"/>, which is shared, admin-owned state edited on /settings —
/// anonymous visitors can reach the picker.
/// </para>
/// </summary>
public class SeasonState(SeasonService seasons)
{
    private Task? _loading;

    public List<Season> Seasons { get; private set; } = [];

    /// <summary>The season being filtered by. Null means "all seasons" — no filter.</summary>
    public int? SelectedSeasonId { get; private set; }

    public Season? SelectedSeason => Seasons.FirstOrDefault(s => s.Id == SelectedSeasonId);

    public event Action? OnChanged;

    /// <summary>
    /// Loads the season list once per circuit. MainLayout and the page both need it during their
    /// own <c>OnInitializedAsync</c> and interleave at the first await, so the first caller runs
    /// the query and everyone else awaits that same task.
    /// <para>
    /// This was once load-bearing: the services shared one scoped <c>AppDbContext</c>, and a second
    /// concurrent query on it threw. They take a short-lived context per operation now (see
    /// <c>AddDbContextFactory</c> in Program.cs), so concurrent callers are safe and this is purely
    /// an optimisation — a page that forgets it costs a duplicate query, not a crash.
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
        SelectedSeasonId = Seasons.FirstOrDefault(s => s.IsCurrent)?.Id
            ?? Seasons.FirstOrDefault()?.Id;
    }

    public void Select(int? seasonId)
    {
        if (SelectedSeasonId == seasonId) return;

        SelectedSeasonId = seasonId;
        OnChanged?.Invoke();
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
