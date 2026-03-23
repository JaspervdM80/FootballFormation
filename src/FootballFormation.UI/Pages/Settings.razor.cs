using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class Settings
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ILogger<Settings> Logger { get; set; } = null!;

    private MatchPreferences? _prefs;
    private MudForm _form = null!;
    private DateTime? _nextMatchDate;

    protected override async Task OnInitializedAsync()
    {
        var prefsResult = await PreferencesService.GetAsync();
        if (prefsResult.IsSuccess)
        {
            _prefs = prefsResult.Value;
        }
        else
        {
            Snackbar.Add(prefsResult.Error!, Severity.Error);
            return;
        }

        var dateResult = await PreferencesService.GetNextMatchDateAsync();
        if (dateResult.IsSuccess)
        {
            _nextMatchDate = dateResult.Value;
        }
    }

    private async Task Save()
    {
        if (_prefs is null) return;

        var saveResult = await PreferencesService.SaveAsync(_prefs);
        if (saveResult.IsSuccess)
        {
            Snackbar.Add("Preferences saved!", Severity.Success);

            var dateResult = await PreferencesService.GetNextMatchDateAsync();
            if (dateResult.IsSuccess)
            {
                _nextMatchDate = dateResult.Value;
            }
        }
        else
        {
            Snackbar.Add(saveResult.Error!, Severity.Error);
        }
    }
}
