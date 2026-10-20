using FootballFormation.UI.State;

namespace FootballFormation.UI.Components;

/// Hidden while the deployment serves one team, which is the app exactly as it looked before there was anything to choose.
public partial class TeamPicker
{
    [Inject] private TeamState Team { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    protected override Task OnInitializedAsync() => Team.EnsureLoadedAsync();

    private string SetUrl(Team team) =>
        AppRoutes.SetTeam(team, AppNav.AfterTeamChange(Navigation.ToBaseRelativePath(Navigation.Uri)));
}
