using FootballFormation.UI.State;
using FootballFormation.UI.Theming;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Layout;

public partial class MainLayout
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private TeamState Team { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Team.EnsureLoadedAsync();

        if (AuthState is not null && (await AuthState).User.MustChangePassword())
            EnforcePasswordChange();
    }

    /// What back.js records this page as, for a back arrow on a later one to name. Null on a page the app cannot name, which the arrow
    /// then steps past.
    private string? PageName => AppNav.PageNameKey(CurrentPath) is { } key ? L[key].Value : null;

    private string CurrentPath => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

    /// The visible half of the rule only: such a session is not an admin as far as the services are concerned either (see ICurrentUser).
    /// Once per initialization is once per navigation, because the layout renders statically and the router rebuilds it for every page.
    private void EnforcePasswordChange()
    {
        if (CurrentPath.StartsWith(AppRoutes.Settings, StringComparison.OrdinalIgnoreCase)) return;

        Navigation.NavigateTo(AppRoutes.Settings, replace: true);
    }

    // The same ClubTheme record that emits the CSS custom properties, so MudBlazor and the hand-written styles cannot drift apart.
    private static readonly MudTheme Theme = ClubTheme.Current.ToMudTheme();
}
