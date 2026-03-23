namespace FootballFormation.Core.Models;

public class MatchPreferences
{
    public int Id { get; set; }
    public int GameDurationMinutes { get; set; } = 60;
    public GameSplitType DefaultSplitType { get; set; } = GameSplitType.Halves;
    public FormationType DefaultFormation { get; set; } = FormationType.F442;
    public DayOfWeek MatchDay { get; set; } = DayOfWeek.Saturday;
}
