using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using FootballFormation.UI.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace FootballFormation.UI.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private bool _drawerOpen;
    private bool _mustChangePassword;

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    protected override async Task OnInitializedAsync()
    {
        // The layout is on screen before any page, which is the only moment early enough to catch
        // every navigation in the circuit — see NavigationTrail.Start.
        Trail.Start();
        Navigation.LocationChanged += OnLocationChanged;

        if (AuthState is not null)
            _mustChangePassword = (await AuthState).User.MustChangePassword();

        EnforcePasswordChange();
    }

    // The drawer's nav links are plain anchors; close the drawer when one navigates
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        EnforcePasswordChange();

        if (!_drawerOpen) return;

        _drawerOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Holds an account still on the seeded password on /settings until it picks its own. This is
    /// the visible half of the rule; the enforcing half is that such a session is not an admin as
    /// far as the services are concerned, so nothing it might reach another way would work either
    /// (see ICurrentUser). Changing the password rolls the security stamp, which drops the cookie
    /// carrying the claim — so the gate cannot outlast the condition.
    /// </summary>
    private void EnforcePasswordChange()
    {
        if (!_mustChangePassword) return;

        var path = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        if (path.StartsWith(AppRoutes.Settings, StringComparison.OrdinalIgnoreCase)) return;

        Navigation.NavigateTo(AppRoutes.Settings, replace: true);
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
