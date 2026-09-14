using System.Globalization;
using FootballFormation.UI.State;

namespace FootballFormation.UI.Pages;

public partial class Preferences
{
    [Inject] private MatchPreferencesService PreferencesService { get; set; } = null!;
    [Inject] private SeasonService SeasonService { get; set; } = null!;
    [Inject] private SeasonState SeasonState { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private MatchPreferences? _prefs;
    private DateTime? _nextMatchDate;
    private List<Season>? _seasons;

    /// Starts on whatever the app bar picker shows but is its own choice, so an admin can set next season's game length from here.
    private int _prefsSeasonId;

    private Season? PrefsSeason => _seasons?.FirstOrDefault(s => s.Id == _prefsSeasonId);

    /// Read off the default shape rather than stored beside it, so the two pickers cannot disagree about how many a side the season plays.
    private MatchFormat DefaultMatchFormat =>
        _prefs?.DefaultFormation.Format() ?? MatchFormat.ElevenASide;

    private void OnDefaultMatchFormatChanged(MatchFormat format)
    {
        if (_prefs is not null) _prefs.DefaultFormation = format.DefaultFormation();
    }

    protected override async Task OnInitializedAsync()
    {
        await SeasonState.EnsureLoadedAsync();

        var result = await SeasonService.GetAllAsync(Cancellation);
        _seasons = Snackbar.ReportFailure(L, result) ? result.Value : [];

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

    /// The dropdown items say it in the ambient culture; without this the collapsed field falls back to DayOfWeek.ToString().
    private static string DayName(DayOfWeek day) => CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(day);

    private async Task Save()
    {
        if (_prefs is null) return;

        var saveResult = await PreferencesService.SaveAsync(_prefs);
        if (!Snackbar.Report(L, saveResult, L["Preferences for {0} saved!", PrefsSeason?.Name ?? ""])) return;

        // The row carries the training period too, so a save from here can still write sessions — say so rather than leave it silent.
        if (saveResult.Value is { IsEmpty: false } sync)
            Snackbar.Add(L["{0} trainings created, {1} removed", sync.Created, sync.Removed], Severity.Info);

        await RefreshNextMatchDate();
    }

    private async Task RefreshNextMatchDate()
    {
        var matchResult = await PreferencesService.GetNextMatchDateAsync(_prefsSeasonId, Cancellation);
        if (matchResult.IsSuccess) _nextMatchDate = matchResult.Value;
    }
}
