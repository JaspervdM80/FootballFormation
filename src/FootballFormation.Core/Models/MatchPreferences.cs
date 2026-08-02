namespace FootballFormation.Core.Models;

/// <summary>
/// The defaults a new game starts from — one row **per season**, not one row for the app. A team
/// moving up an age group plays longer games and often a different shape, and the fixture day can
/// move too, so last season's defaults must stay attached to last season's games rather than being
/// overwritten when this year's are set.
/// </summary>
public class MatchPreferences
{
    public int Id { get; set; }

    /// <summary>FK → Season. Unique: exactly one preferences row per season, created on first
    /// read by <c>MatchPreferencesService.GetAsync</c>.</summary>
    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int GameDurationMinutes { get; set; } = 60;
    public GameSplitType DefaultSplitType { get; set; } = GameSplitType.Halves;
    public FormationType DefaultFormation { get; set; } = FormationType.F442;
    public DayOfWeek MatchDay { get; set; } = DayOfWeek.Saturday;

    /// <summary>A copy of these defaults for another season — how a season without a row of its
    /// own is seeded, so a new season inherits last year's settings instead of the hardcoded ones.</summary>
    public MatchPreferences CopyFor(int seasonId) => new()
    {
        SeasonId = seasonId,
        GameDurationMinutes = GameDurationMinutes,
        DefaultSplitType = DefaultSplitType,
        DefaultFormation = DefaultFormation,
        MatchDay = MatchDay
    };
}
