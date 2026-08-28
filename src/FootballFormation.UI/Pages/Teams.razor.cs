using FootballFormation.UI.State;

namespace FootballFormation.UI.Pages;

public partial class Teams
{
    [Inject] private TeamService TeamService { get; set; } = null!;
    [Inject] private TeamState Team { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private List<Team>? _teams;
    private List<Club>? _clubs;
    private List<Team> _clubTeams = [];

    /// What the app bar was rendered with, so a rename here can tell whether it left the chrome behind.
    private string _renderedChrome = string.Empty;

    /// The team the app is showing, which the picker below does not change — it scopes this page only.
    private int? _currentTeamId;

    private int _selectedClubId;

    private string Chrome => $"{Team.DisplayName}|{Team.LogoUrl}";

    private Club? SelectedClub => _clubs?.FirstOrDefault(c => c.Id == _selectedClubId);

    private string TeamCount => _clubTeams.Count == 1
        ? L["{0} team", _clubTeams.Count]
        : L["{0} teams", _clubTeams.Count];

    protected override async Task OnInitializedAsync()
    {
        await Team.EnsureLoadedAsync();
        _renderedChrome = Chrome;

        await Load();
    }

    private async Task Load()
    {
        var teams = await TeamService.GetTeamsAsync(Cancellation);
        _teams = Snackbar.ReportFailure(L, teams) ? teams.Value : [];

        var clubs = await TeamService.GetClubsAsync(Cancellation);
        _clubs = Snackbar.ReportFailure(L, clubs) ? clubs.Value : [];

        _currentTeamId = Team.Current?.Id;

        SelectClub(ResolveSelectedClub(_clubs ?? []));
    }

    private void SelectClub(int clubId)
    {
        _selectedClubId = clubId;
        _clubTeams = _teams?.Where(t => t.ClubId == clubId).ToList() ?? [];
    }

    /// Keeps the club in view across a reload, falling back to the club of the team the app is showing.
    private int ResolveSelectedClub(List<Club> clubs)
    {
        if (clubs.Any(c => c.Id == _selectedClubId)) return _selectedClubId;

        var current = Team.Current?.ClubId;
        if (current is int clubId && clubs.Any(c => c.Id == clubId)) return clubId;

        return clubs.Count > 0 ? clubs[0].Id : 0;
    }

    /// MainLayout renders statically in the page-load request, so a rename made here never reaches the app bar on its own. Reloading is
    /// the only way to redraw it, so it happens when the name or the crest actually moved rather than after every edit.
    private async Task Reload()
    {
        await Team.RefreshAsync();
        await Load();

        if (Chrome != _renderedChrome) Navigation.NavigateTo(AppRoutes.Teams, forceLoad: true);
    }

    private async Task OpenAddTeamDialog()
    {
        var edited = await ShowTeamDialogAsync(L["Add Team"]);
        if (edited is null) return;

        var result = await TeamService.CreateTeamAsync(new Team { ClubId = edited.ClubId, Name = edited.Name });

        Snackbar.Report(L, result, L["Team {0} created", edited.Name]);
        await Reload();
    }

    private async Task OpenEditTeamDialog(Team team)
    {
        var edited = await ShowTeamDialogAsync(L["Edit Team"], team);
        if (edited is null) return;

        var result = await TeamService.UpdateTeamAsync(
            new Team { Id = team.Id, ClubId = edited.ClubId, Name = edited.Name });

        Snackbar.Report(L, result, L["Team {0} updated", edited.Name]);
        await Reload();
    }

    private async Task DeleteTeam(Team team)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Team"],
            L["Are you sure you want to delete {0}?", team.FullName]);
        if (!confirmed) return;

        var result = await TeamService.DeleteTeamAsync(team.Id);
        Snackbar.Report(L, result, L["Team {0} deleted", team.Name], Severity.Warning);
        await Reload();
    }

    private async Task OpenAddClubDialog()
    {
        var edited = await ShowClubDialogAsync(L["Add Club"]);
        if (edited is null) return;

        var result = await TeamService.CreateClubAsync(
            new Club { Name = edited.Name, LogoUrl = edited.LogoUrl, ThemeName = edited.ThemeName });

        if (Snackbar.Report(L, result, L["Club {0} created", edited.Name]) && result.Value is { } created)
        {
            _selectedClubId = created.Id;
        }

        await Reload();
    }

    private async Task OpenEditClubDialog(Club club)
    {
        var edited = await ShowClubDialogAsync(L["Edit Club"], club);
        if (edited is null) return;

        var result = await TeamService.UpdateClubAsync(
            new Club { Id = club.Id, Name = edited.Name, LogoUrl = edited.LogoUrl, ThemeName = edited.ThemeName });

        Snackbar.Report(L, result, L["Club {0} updated", edited.Name]);
        await Reload();
    }

    private async Task DeleteClub(Club club)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Club"],
            L["Are you sure you want to delete {0}?", club.Name]);
        if (!confirmed) return;

        var result = await TeamService.DeleteClubAsync(club.Id);
        Snackbar.Report(L, result, L["Club {0} deleted", club.Name], Severity.Warning);
        await Reload();
    }

    /// Null when the dialog was cancelled.
    private Task<TeamDialog.Model?> ShowTeamDialogAsync(string title, Team? team = null) =>
        DialogService.PromptAsync<TeamDialog, TeamDialog.Model>(title, p =>
        {
            p.Add(x => x.Clubs, _clubs ?? []);
            p.Add(x => x.DefaultClubId, _selectedClubId);
            if (team is not null) p.Add(x => x.Team, team);
        });

    /// <inheritdoc cref="ShowTeamDialogAsync"/>
    private Task<ClubDialog.Model?> ShowClubDialogAsync(string title, Club? club = null) =>
        DialogService.PromptAsync<ClubDialog, ClubDialog.Model>(title, p =>
        {
            if (club is not null) p.Add(x => x.Club, club);
        });
}
