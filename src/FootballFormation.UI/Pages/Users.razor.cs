using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

public partial class Users
{
    [Inject] private UserService UserService { get; set; } = null!;
    [Inject] private PasswordLinkService PasswordLinks { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private TeamService TeamService { get; set; } = null!;
    [Inject] private TeamState Team { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private List<UserSummary>? _users;

    /// Who is looking, so the list can mark their own row and hide its delete action.
    private int? _currentUserId;

    /// The role dialog offers ApplicationAdmin only to someone who already holds it — UserService refuses the rest.
    private bool _canGrantApplicationAdmin;

    /// Named on every row and picked in the dialog, so the same list serves both.
    private List<Team> _teams = [];

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _currentUserId = authState.User.UserId();
        _canGrantApplicationAdmin = authState.User.IsApplicationAdmin();

        await Team.EnsureLoadedAsync();
        await LoadTeams(authState.User.AdminTeamId());
        await LoadUsers();
    }

    /// An ordinary admin may only put an account on the team they run, so the picker never offers one UserService would refuse.
    private async Task LoadTeams(int? adminTeamId)
    {
        var result = await TeamService.GetTeamsAsync(Cancellation);
        if (!Snackbar.ReportFailure(L, result)) return;

        _teams = _canGrantApplicationAdmin
            ? result.Value!
            : result.Value!.Where(t => t.Id == adminTeamId).ToList();
    }

    private string TeamName(int? teamId) => _teams.FirstOrDefault(t => t.Id == teamId)?.FullName ?? string.Empty;

    private async Task LoadUsers()
    {
        var result = await UserService.GetAllAsync(Cancellation);
        _users = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

    private async Task OpenAddDialog()
    {
        var edited = await ShowUserDialogAsync(L["Add User"]);
        if (edited is null) return;

        var withoutPassword = string.IsNullOrEmpty(edited.Password);
        var result = await UserService.CreateAsync(
            edited.DisplayName, edited.Username, withoutPassword ? null : edited.Password, edited.Role, edited.TeamId, edited.Email);

        Snackbar.Report(L, result, L["User {0} created", edited.DisplayName]);
        await LoadUsers();

        // Nobody can sign in to it yet, so the link that lets them is the next thing the admin needs. Rendered first: this handler
        // stays open until the link dialog closes, and the new row would wait with it.
        if (result.IsSuccess && withoutPassword)
        {
            StateHasChanged();
            await ShowSignInLink(result.Value!.Id, result.Value.DisplayName);
        }
    }

    private async Task OpenEditDialog(UserSummary user)
    {
        var edited = await ShowUserDialogAsync(L["Edit User"], user);
        if (edited is null) return;

        var result = await UserService.UpdateAsync(
            user.Id, edited.DisplayName, edited.Username, edited.Role, edited.TeamId, edited.Email);

        Snackbar.Report(L, result, L["User {0} updated", edited.DisplayName]);
        await LoadUsers();
    }

    /// Signs that user out of every session they had open, because the security stamp changes — which is the point.
    private async Task ResetPassword(UserSummary user)
    {
        var edited = await ShowUserDialogAsync(L["Reset Password"], user, passwordOnly: true);
        if (edited is null) return;

        var result = await UserService.SetPasswordAsync(user.Id, edited.Password);
        Snackbar.Report(L, result, L["Password reset for {0}", user.DisplayName]);
        await LoadUsers();
    }

    /// The link is shown rather than copied straight away: the clipboard only accepts a copy from the click itself, and this one went
    /// through the circuit to make the link.
    private async Task ShowSignInLink(int userId, string displayName)
    {
        var result = await PasswordLinks.CreateLinkAsync(userId);
        if (!Snackbar.ReportFailure(L, result)) return;

        var dialog = await DialogService.ShowAsync<SignInLinkDialog>(L["Sign-in link"], new DialogParameters<SignInLinkDialog>
        {
            { x => x.DisplayName, displayName },
            { x => x.Url, AppRoutes.PasswordLinkUrl(Navigation.BaseUri, result.Value!) }
        }, UiFeedback.LockedDialog);
        await dialog.Result;
    }

    private async Task DeleteUser(UserSummary user)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete User"],
            L["Are you sure you want to delete the account for {0}?", user.DisplayName]);
        if (!confirmed) return;

        var result = await UserService.DeleteAsync(user.Id);
        Snackbar.Report(L, result, L["User {0} deleted", user.DisplayName], Severity.Warning);
        await LoadUsers();
    }

    /// Null when the dialog was cancelled.
    private Task<UserDialog.Model?> ShowUserDialogAsync(
        string title, UserSummary? user = null, bool passwordOnly = false) =>
        DialogService.PromptAsync<UserDialog, UserDialog.Model>(title, p =>
        {
            if (user is not null) p.Add(x => x.User, user);
            p.Add(x => x.PasswordOnly, passwordOnly);
            p.Add(x => x.CanGrantApplicationAdmin, _canGrantApplicationAdmin);
            p.Add(x => x.Teams, _teams);
            p.Add(x => x.DefaultTeamId, Team.Current?.Id);
        });
}
