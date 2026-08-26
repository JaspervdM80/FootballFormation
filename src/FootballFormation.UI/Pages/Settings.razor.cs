using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

public partial class Settings
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private UserService UserService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    private MatchPreferences? _prefs;
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

    /// <summary>The signed-in account is still on the password a fresh install seeded. MainLayout
    /// has pinned them to this page; the card explains why.</summary>
    private bool _mustChangePassword;

    protected override async Task OnInitializedAsync()
    {
        _mustChangePassword = (await AuthStateTask).User.MustChangePassword();

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

        var prefsResult = await PreferencesService.GetAsync(_prefsSeasonId, Cancellation);
        if (!Snackbar.ReportFailure(L, prefsResult)) return;

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
        var result = await SeasonService.GetAllAsync(Cancellation);
        _seasons = Snackbar.ReportFailure(L, result) ? result.Value : [];
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
        Snackbar.Report(L, result, L["Season {0} created", season.Name]);
        await ReloadSeasons();
    }

    private async Task OpenEditSeasonDialog(Season season)
    {
        var updated = await ShowSeasonDialogAsync(L["Edit Season"], season);
        if (updated is null) return;

        var result = await SeasonService.UpdateAsync(updated);
        Snackbar.Report(L, result, L["Season {0} updated", updated.Name]);
        await ReloadSeasons();
    }

    private async Task DeleteSeason(Season season)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Season"],
            L["Are you sure you want to delete the season {0}?", season.Name]);
        if (!confirmed) return;

        var result = await SeasonService.DeleteAsync(season.Id);
        Snackbar.Report(L, result, L["Season {0} deleted", season.Name], Severity.Warning);
        await ReloadSeasons();
    }

    private async Task SetCurrentSeason(Season season)
    {
        var result = await SeasonService.SetCurrentAsync(season.Id);
        Snackbar.Report(L, result, L["Season {0} is now the current season", season.Name]);
        await ReloadSeasons();
    }

    /// <summary>Returns the edited season, or null when the dialog was cancelled.</summary>
    private async Task<Season?> ShowSeasonDialogAsync(string title, Season? season = null)
    {
        return await DialogService.PromptAsync<SeasonDialog, Season>(title, p =>
        {
            if (season is not null) p.Add(x => x.Season, season);
        });
    }

    private async Task Save()
    {
        if (_prefs is null) return;

        var saveResult = await PreferencesService.SaveAsync(_prefs);
        if (!Snackbar.Report(L, saveResult, L["Preferences for {0} saved!", PrefsSeason?.Name ?? ""])) return;

        await RefreshNextMatchDate();
    }

    private async Task RefreshNextMatchDate()
    {
        var dateResult = await PreferencesService.GetNextMatchDateAsync(_prefsSeasonId, Cancellation);
        if (dateResult.IsSuccess) _nextMatchDate = dateResult.Value;
    }

    private async Task ChangePassword()
    {
        // The three fields are Required="true", so let the form say so in place rather than
        // leaving those attributes decorative — same as GameDialog and UserDialog do.
        await _passwordForm.ValidateAsync();

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

        // ClaimTypes.Name carries the login, which is what ChangePasswordAsync verifies against.
        // No fallback: the page is [Authorize]d, so an absent name means something is wrong with
        // the principal — and defaulting to "admin" would aim the change at the wrong account.
        var authState = await AuthStateTask;
        var username = authState.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            Snackbar.Add(L["Current password is incorrect"], Severity.Error);
            return;
        }

        var result = await UserService.ChangePasswordAsync(username, _currentPassword, _newPassword);
        switch (result)
        {
            case UserService.PasswordChangeResult.Success:
                Snackbar.Add(L["Password changed successfully!"], Severity.Success);
                _currentPassword = "";
                _newPassword = "";
                _confirmPassword = "";

                // The change rolled the security stamp, so this cookie is already dead — the next
                // request would sign them out mid-navigation. Send them to the login form now, on
                // a full reload, so it reads as "sign in again" rather than a session that broke.
                if (_mustChangePassword)
                    Navigation.NavigateTo(AppRoutes.Login, forceLoad: true);
                break;
            case UserService.PasswordChangeResult.InvalidCurrentPassword:
                Snackbar.Add(L["Current password is incorrect"], Severity.Error);
                break;
            case UserService.PasswordChangeResult.PasswordTooShort:
                Snackbar.Add(L["New password must be at least {0} characters", UserService.MinPasswordLength], Severity.Error);
                break;
            case UserService.PasswordChangeResult.PasswordReused:
                Snackbar.Add(L["New password must be different from the current one"], Severity.Error);
                break;
        }
    }
}
