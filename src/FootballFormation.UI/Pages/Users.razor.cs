using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Users
{
    [Inject] private UserService UserService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private List<AppUser>? _users;

    /// <summary>Who is looking, so the list can mark their own row and hide its delete action.</summary>
    private int? _currentUserId;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        _currentUserId = authState.User.UserId();

        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        var result = await UserService.GetAllAsync();
        _users = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

    private async Task OpenAddDialog()
    {
        var edited = await ShowUserDialogAsync(L["Add User"]);
        if (edited is null) return;

        var result = await UserService.CreateAsync(
            edited.DisplayName, edited.Username, edited.Password, edited.Role);

        Snackbar.Report(L, result, L["User {0} created", edited.DisplayName]);
        await LoadUsers();
    }

    private async Task OpenEditDialog(AppUser user)
    {
        var edited = await ShowUserDialogAsync(L["Edit User"], user);
        if (edited is null) return;

        var result = await UserService.UpdateAsync(
            user.Id, edited.DisplayName, edited.Username, edited.Role);

        Snackbar.Report(L, result, L["User {0} updated", edited.DisplayName]);
        await LoadUsers();
    }

    /// <summary>
    /// An admin setting someone else's password without knowing the old one. It signs that user out
    /// of any session they had open — the security stamp changes — which is the point.
    /// </summary>
    private async Task ResetPassword(AppUser user)
    {
        var edited = await ShowUserDialogAsync(L["Reset Password"], user, passwordOnly: true);
        if (edited is null) return;

        var result = await UserService.SetPasswordAsync(user.Id, edited.Password);
        Snackbar.Report(L, result, L["Password reset for {0}", user.DisplayName]);
        await LoadUsers();
    }

    private async Task DeleteUser(AppUser user)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete User"],
            L["Are you sure you want to delete the account for {0}?", user.DisplayName]);
        if (!confirmed) return;

        var result = await UserService.DeleteAsync(user.Id);
        Snackbar.Report(L, result, L["User {0} deleted", user.DisplayName], Severity.Warning);
        await LoadUsers();
    }

    /// <summary>Returns what the dialog produced, or null when it was cancelled.</summary>
    private Task<UserDialog.Model?> ShowUserDialogAsync(
        string title, AppUser? user = null, bool passwordOnly = false) =>
        DialogService.PromptAsync<UserDialog, UserDialog.Model>(title, p =>
        {
            if (user is not null) p.Add(x => x.User, user);
            p.Add(x => x.PasswordOnly, passwordOnly);
        });
}
