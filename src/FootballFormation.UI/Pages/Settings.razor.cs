using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Settings
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private AdminAuthService AuthService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private MatchPreferences? _prefs;
    private MudForm _form = null!;
    private MudForm _passwordForm = null!;
    private DateTime? _nextMatchDate;

    private List<Season>? _seasons;

    /// <summary>The season the defaults card is editing. Starts on whatever the app bar picker is
    /// showing, but it is its own choice: an admin sets next season's game length while still
    /// looking at this season's games.</summary>
    private int _prefsSeasonId;

    private Season? PrefsSeason => _seasons?.FirstOrDefault(s => s.Id == _prefsSeasonId);

    private string _currentPassword = "";
    private string _newPassword = "";
    private string _confirmPassword = "";

    protected override async Task OnInitializedAsync()
    {
        await SeasonState.EnsureLoadedAsync();
        await LoadSeasons();

        _prefsSeasonId = SeasonState.SelectedSeasonId
            ?? _seasons?.FirstOrDefault(s => s.IsCurrent)?.Id
            ?? _seasons?.FirstOrDefault()?.Id
            ?? 0;

        await LoadPreferences();
    }

    private async Task LoadPreferences()
    {
        if (_prefsSeasonId == 0)
        {
            _prefs = null;
            _nextMatchDate = null;
            return;
        }

        var prefsResult = await PreferencesService.GetAsync(_prefsSeasonId);
        if (!Snackbar.ReportFailure(prefsResult)) return;

        _prefs = prefsResult.Value;
        await RefreshNextMatchDate();
    }

    private async Task OnPrefsSeasonChanged(int seasonId)
    {
        _prefsSeasonId = seasonId;
        await LoadPreferences();
    }

    private async Task LoadSeasons()
    {
        var result = await SeasonService.GetAllAsync();
        _seasons = Snackbar.ReportFailure(result) ? result.Value : [];
    }

    /// <summary>Every season mutation refreshes the picker too, so the app bar and this list can't
    /// disagree without a page reload.</summary>
    private async Task ReloadSeasons()
    {
        await LoadSeasons();
        await SeasonState.RefreshAsync();

        // The defaults card may have been editing the season that just went away.
        if (_seasons?.Any(s => s.Id == _prefsSeasonId) != true)
        {
            _prefsSeasonId = _seasons?.FirstOrDefault(s => s.IsCurrent)?.Id
                ?? _seasons?.FirstOrDefault()?.Id
                ?? 0;
            await LoadPreferences();
        }
    }

    private async Task OpenAddSeasonDialog()
    {
        var season = await ShowSeasonDialogAsync(L["New Season"]);
        if (season is null) return;

        var result = await SeasonService.CreateAsync(season);
        Snackbar.Report(result, L["Season {0} created", season.Name]);
        await ReloadSeasons();
    }

    private async Task OpenEditSeasonDialog(Season season)
    {
        var updated = await ShowSeasonDialogAsync(L["Edit Season"], season);
        if (updated is null) return;

        var result = await SeasonService.UpdateAsync(updated);
        Snackbar.Report(result, L["Season {0} updated", updated.Name]);
        await ReloadSeasons();
    }

    private async Task DeleteSeason(Season season)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Season"],
            L["Are you sure you want to delete the season {0}?", season.Name]);
        if (!confirmed) return;

        var result = await SeasonService.DeleteAsync(season.Id);
        Snackbar.Report(result, L["Season {0} deleted", season.Name], Severity.Warning);
        await ReloadSeasons();
    }

    private async Task SetCurrentSeason(Season season)
    {
        var result = await SeasonService.SetCurrentAsync(season.Id);
        Snackbar.Report(result, L["Season {0} is now the current season", season.Name]);
        await ReloadSeasons();
    }

    /// <summary>Returns the edited season, or null when the dialog was cancelled.</summary>
    private async Task<Season?> ShowSeasonDialogAsync(string title, Season? season = null)
    {
        var parameters = new DialogParameters<SeasonDialog>();
        if (season is not null) parameters.Add(x => x.Season, season);

        var dialog = await DialogService.ShowAsync<SeasonDialog>(title, parameters, UiFeedback.LockedDialog);
        var result = await dialog.Result;

        return result is { Canceled: false, Data: Season edited } ? edited : null;
    }

    private async Task Save()
    {
        if (_prefs is null) return;

        var saveResult = await PreferencesService.SaveAsync(_prefs);
        if (!Snackbar.Report(saveResult, L["Preferences for {0} saved!", PrefsSeason?.Name ?? ""])) return;

        await RefreshNextMatchDate();
    }

    private async Task RefreshNextMatchDate()
    {
        var dateResult = await PreferencesService.GetNextMatchDateAsync(_prefsSeasonId);
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
