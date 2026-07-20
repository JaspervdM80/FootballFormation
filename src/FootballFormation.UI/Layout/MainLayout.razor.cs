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

    // Club colors: keep in sync with wwwroot/theme.css (GJS Gorinchem, light) until
    // the theme is driven by shared configuration. Text/line shades derive from the
    // same ink color (#182b1f) as the CSS --ink token.
    private static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#e11d24",
            PrimaryContrastText = "#ffffff",
            Secondary = "#0a8f3d",
            Tertiary = "#c8151c",
            AppbarBackground = "#ffffff",
            AppbarText = "rgba(24,43,31,0.85)",
            Surface = "#eef7f1",
            Background = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "rgba(24,43,31,0.8)",
            TextPrimary = "rgba(24,43,31,0.92)",
            TextSecondary = "rgba(24,43,31,0.6)",
            ActionDefault = "rgba(24,43,31,0.55)",
            ActionDisabled = "rgba(24,43,31,0.25)",
            Divider = "rgba(24,43,31,0.1)",
            TableHover = "rgba(24,43,31,0.04)",
            TableStriped = "rgba(24,43,31,0.02)",
            LinesDefault = "rgba(24,43,31,0.12)",
            OverlayDark = "rgba(0,0,0,0.35)"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px"
        }
    };
}
