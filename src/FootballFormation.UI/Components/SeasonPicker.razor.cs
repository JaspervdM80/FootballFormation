using FootballFormation.UI.State;

namespace FootballFormation.UI.Components;

/// <summary>
/// The global season filter. Rendered twice — once in the app bar and once in the mobile drawer —
/// from the same component so the two can't drift apart.
/// <para>
/// Every entry is a link to <c>/season/set</c>, which stores the choice and sends the visitor back
/// to the page they were on. Nothing here subscribes to anything: this renders in the layout, which
/// is static on every page, and the new season arrives on the request that follows the click.
/// </para>
/// </summary>
public partial class SeasonPicker
{
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// <summary>Shortens the label to "25/26" for the crowded app bar.</summary>
    [Parameter] public bool Compact { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // The start page shows the picker but filters nothing, so no page loads the list there.
        // Memoized in SeasonState, so on the season-aware pages this shares the page's own query.
        await SeasonState.EnsureLoadedAsync();
    }

    /// <summary>Which routes those are is AppNav's to know — the same list decides what the back
    /// button calls each page, and it only stays right while there is one copy of it.</summary>
    private bool Visible => AppNav.IsSeasonAware(CurrentPath);

    private string CurrentPath =>
        Navigation.ToBaseRelativePath(Navigation.Uri).Split('?')[0].Trim('/');

    private string Label => SeasonState.SelectedSeason is { } season
        ? (Compact ? season.ShortName : season.Name)
        : L["All seasons"];

    // The query string is kept, so returning to a filtered list returns to the filter too.
    private string SetUrl(int? seasonId) =>
        AppRoutes.SetSeason(seasonId, "/" + Navigation.ToBaseRelativePath(Navigation.Uri));
}
