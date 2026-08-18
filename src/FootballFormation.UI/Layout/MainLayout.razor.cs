using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using FootballFormation.UI.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

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

    /// <summary>
    /// Holds an account still on the seeded password on /settings until it picks its own. This is
    /// the visible half of the rule; the enforcing half is that such a session is not an admin as
    /// far as the services are concerned, so nothing it might reach another way would work either
    /// (see ICurrentUser). Changing the password rolls the security stamp, which drops the cookie
    /// carrying the claim — so the gate cannot outlast the condition.
    /// <para>
    /// Once per initialization is once per navigation: the layout is statically rendered, so the
    /// router builds it again for every page rather than keeping one across a circuit's lifetime.
    /// That is what replaced the <c>LocationChanged</c> subscription this used to need.
    /// </para>
    /// </summary>
    private void EnforcePasswordChange()
    {
        var path = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        if (path.StartsWith(AppRoutes.Settings, StringComparison.OrdinalIgnoreCase)) return;

        Navigation.NavigateTo(AppRoutes.Settings, replace: true);
    }

    // Full reload on purpose: the circuit's culture is fixed at startup, so the cookie set by
    // /culture/set only takes effect on a fresh page load. A link gets that for free.
    private string CultureUrl(string culture) =>
        AppRoutes.SetCulture(culture, "/" + Navigation.ToBaseRelativePath(Navigation.Uri));

    // Built from the same ClubTheme record that emits the CSS custom properties, so MudBlazor's
    // components and the hand-written styles can no longer drift apart.
    private static readonly MudTheme Theme = ClubTheme.Current.ToMudTheme();
}
