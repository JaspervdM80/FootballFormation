using FootballFormation.UI.State;

namespace FootballFormation.UI.Components;

/// Every entry is a link to /season/set rather than a handler: this renders in the layout, which is static on every page, and the new
/// season arrives on the request that follows the click.
public partial class SeasonPicker
{
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// Shortens the label to "25/26" for the crowded app bar.
    [Parameter] public bool Compact { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // The start page shows the picker but filters nothing, so no page loads the list there. Memoized in SeasonState, so elsewhere
        // this shares the page's own query.
        await SeasonState.EnsureLoadedAsync();
    }

    /// Which routes those are is AppNav's to know — the same list names each page for the back button, and one copy is what keeps it right.
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
