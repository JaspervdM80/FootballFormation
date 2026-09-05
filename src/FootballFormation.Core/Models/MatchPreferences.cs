namespace FootballFormation.Core.Models;

/// One row per season, not one for the app: a team moving up an age group plays longer games in a different shape, and last season's
/// defaults have to stay attached to last season's games.
public class MatchPreferences
{
    public int Id { get; set; }

    /// Denormalised from the season so the team query filter reads one column, never a join. Set from the season at creation. See Season.TeamId.
    public int TeamId { get; set; }

    /// Unique: exactly one row per season, created on first read by MatchPreferencesService.GetAsync.
    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int GameDurationMinutes { get; set; } = 60;
    public GameSplitType DefaultSplitType { get; set; } = GameSplitType.Halves;
    public FormationType DefaultFormation { get; set; } = FormationType.F442;
    public DayOfWeek MatchDay { get; set; } = DayOfWeek.Saturday;

    /// Empty until an admin picks them, which is what makes the next-training date fall back to today rather than to a guessed weekday.
    public List<DayOfWeek> TrainingDays { get; set; } = [];

    /// Null for either end means the season's own window, which is what every row held before the period existed.
    public DateTime? FirstTrainingDate { get; set; }
    public DateTime? LastTrainingDate { get; set; }

    /// How a season without a row of its own is seeded, so it inherits last year's settings instead of the hardcoded ones. The training
    /// period is deliberately left behind, unlike the training days: a date belongs to one season, and last year's opening night is not
    /// a sensible guess at this year's.
    public MatchPreferences CopyFor(int seasonId, int teamId) => new()
    {
        SeasonId = seasonId,
        TeamId = teamId,
        GameDurationMinutes = GameDurationMinutes,
        DefaultSplitType = DefaultSplitType,
        DefaultFormation = DefaultFormation,
        MatchDay = MatchDay,
        TrainingDays = [.. TrainingDays]
    };
}
