using FootballFormation.UI.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace FootballFormation.UI.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private bool _drawerOpen;

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    // The drawer's nav links are plain anchors; close the drawer when one navigates
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (!_drawerOpen) return;

        _drawerOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    // Full reload on purpose: the circuit's culture is fixed at startup, so the
    // cookie set by /culture/set only takes effect on a fresh page load.
    private void SwitchCulture(string culture)
    {
        var returnUrl = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        Navigation.NavigateTo(
            $"/culture/set?culture={culture}&redirectUri={Uri.EscapeDataString(returnUrl)}",
            forceLoad: true);
    }

    // Built from the same ClubTheme record that emits the CSS custom properties, so MudBlazor's
    // components and the hand-written styles can no longer drift apart.
    private static readonly MudTheme Theme = ClubTheme.Current.ToMudTheme();
}
