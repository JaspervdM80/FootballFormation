using FootballFormation.UI.Navigation;
using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace FootballFormation.UI.Components;

/// <summary>
/// The global season filter. Rendered twice — once in the app bar and once in the mobile drawer —
/// from the same component so the two can't drift apart.
/// </summary>
public partial class SeasonPicker : IDisposable
{
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// <summary>Shortens the label to "25/26" for the crowded app bar.</summary>
    [Parameter] public bool Compact { get; set; }

    protected override async Task OnInitializedAsync()
    {
        SeasonState.OnChanged += OnSeasonChanged;
        Navigation.LocationChanged += OnLocationChanged;

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

    private void OnSeasonChanged() => _ = InvokeAsync(StateHasChanged);

    // Visibility depends on the route, so a navigation has to re-render us too.
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        SeasonState.OnChanged -= OnSeasonChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
