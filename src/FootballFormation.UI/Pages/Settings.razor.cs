using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Settings
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private MatchPreferences? _prefs;
    private MudForm _form = null!;
    private DateTime? _nextMatchDate;

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
        if (!Snackbar.Report(saveResult, "Preferences saved!")) return;

        await RefreshNextMatchDate();
    }

    private async Task RefreshNextMatchDate()
    {
        var dateResult = await PreferencesService.GetNextMatchDateAsync();
        if (dateResult.IsSuccess) _nextMatchDate = dateResult.Value;
    }
}
