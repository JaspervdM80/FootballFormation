using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Settings
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private AdminAuthService AuthService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private MatchPreferences? _prefs;
    private MudForm _form = null!;
    private MudForm _passwordForm = null!;
    private DateTime? _nextMatchDate;

    private string _currentPassword = "";
    private string _newPassword = "";
    private string _confirmPassword = "";

    protected override async Task OnInitializedAsync()
    {
        var prefsResult = await PreferencesService.GetAsync();
        if (!Snackbar.ReportFailure(prefsResult)) return;

        _prefs = prefsResult.Value;
        await RefreshNextMatchDate();
    }

    private async Task Save()
    {
        if (_prefs is null) return;

        var saveResult = await PreferencesService.SaveAsync(_prefs);
        if (!Snackbar.Report(saveResult, L["Preferences saved!"])) return;

        await RefreshNextMatchDate();
    }

    private async Task RefreshNextMatchDate()
    {
        var dateResult = await PreferencesService.GetNextMatchDateAsync();
        if (dateResult.IsSuccess) _nextMatchDate = dateResult.Value;
    }

    private async Task ChangePassword()
    {
        if (string.IsNullOrWhiteSpace(_currentPassword) || string.IsNullOrWhiteSpace(_newPassword))
        {
            Snackbar.Add(L["Please fill in all password fields"], Severity.Warning);
            return;
        }

        if (_newPassword != _confirmPassword)
        {
            Snackbar.Add(L["New passwords do not match"], Severity.Error);
            return;
        }

        var authState = await AuthStateTask;
        var username = authState.User.Identity?.Name ?? "admin";

        var result = await AuthService.ChangePasswordAsync(username, _currentPassword, _newPassword);
        switch (result)
        {
            case AdminAuthService.PasswordChangeResult.Success:
                Snackbar.Add(L["Password changed successfully!"], Severity.Success);
                _currentPassword = "";
                _newPassword = "";
                _confirmPassword = "";
                break;
            case AdminAuthService.PasswordChangeResult.InvalidCurrentPassword:
                Snackbar.Add(L["Current password is incorrect"], Severity.Error);
                break;
            case AdminAuthService.PasswordChangeResult.PasswordTooShort:
                Snackbar.Add(L["New password must be at least {0} characters", AdminAuthService.MinPasswordLength], Severity.Error);
                break;
            case AdminAuthService.PasswordChangeResult.PasswordReused:
                Snackbar.Add(L["New password must be different from the current one"], Severity.Error);
                break;
        }
    }
}
