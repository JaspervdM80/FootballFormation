namespace FootballFormation.Core.Models;

/// One row per season, not one for the app: a team moving up an age group plays longer games in a different shape, and last season's
/// defaults have to stay attached to last season's games.
public class MatchPreferences
{
    public int Id { get; set; }

    /// Unique: exactly one row per season, created on first read by MatchPreferencesService.GetAsync.
    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int GameDurationMinutes { get; set; } = 60;
    public GameSplitType DefaultSplitType { get; set; } = GameSplitType.Halves;
    public FormationType DefaultFormation { get; set; } = FormationType.F442;
    public DayOfWeek MatchDay { get; set; } = DayOfWeek.Saturday;

    /// Empty until an admin picks them, which is what makes the next-training date fall back to today rather than to a guessed weekday.
    public List<DayOfWeek> TrainingDays { get; set; } = [];

    /// How a season without a row of its own is seeded, so it inherits last year's settings instead of the hardcoded ones.
    public MatchPreferences CopyFor(int seasonId) => new()
    {
        SeasonId = seasonId,
        GameDurationMinutes = GameDurationMinutes,
        DefaultSplitType = DefaultSplitType,
        DefaultFormation = DefaultFormation,
        MatchDay = MatchDay,
        TrainingDays = [.. TrainingDays]
    };
}
