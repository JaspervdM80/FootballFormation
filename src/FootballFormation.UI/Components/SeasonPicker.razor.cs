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

    protected override void OnInitialized()
    {
        SeasonState.OnChanged += OnSeasonChanged;
        Navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>Only shown where a season filter actually changes what is on screen: the games
    /// list and the two stats pages. Elsewhere it would be inert, and on /settings it would be
    /// actively confusing while the admin edits the season list itself.</summary>
    private bool Visible => IsSeasonAware(CurrentPath);

    private static bool IsSeasonAware(string path) =>
        path is "games" or "stats"
        || (path.StartsWith("players/") && path.EndsWith("/stats"));

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
