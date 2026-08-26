using FootballFormation.UI.Theming;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Layout;

public partial class MainLayout
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null && (await AuthState).User.MustChangePassword())
            EnforcePasswordChange();
    }

    /// The visible half of the rule only: such a session is not an admin as far as the services are concerned either (see ICurrentUser).
    /// Once per initialization is once per navigation, because the layout renders statically and the router rebuilds it for every page.
    private void EnforcePasswordChange()
    {
        var path = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        if (path.StartsWith(AppRoutes.Settings, StringComparison.OrdinalIgnoreCase)) return;

        Navigation.NavigateTo(AppRoutes.Settings, replace: true);
    }

    // A link, not a handler: the circuit's culture is fixed at startup, so /culture/set only takes effect on a fresh page load.
    private string CultureUrl(string culture) =>
        AppRoutes.SetCulture(culture, "/" + Navigation.ToBaseRelativePath(Navigation.Uri));

    // The same ClubTheme record that emits the CSS custom properties, so MudBlazor and the hand-written styles cannot drift apart.
    private static readonly MudTheme Theme = ClubTheme.Current.ToMudTheme();
}
