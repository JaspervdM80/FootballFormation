namespace FootballFormation.Core.Models;

public class Game
{
    public int Id { get; set; }
    public required string Opponent { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public FormationType FormationType { get; set; }
    public GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    public int GameDurationMinutes { get; set; } = 60;

    public int? ScoreHome { get; set; }
    public int? ScoreAway { get; set; }

    public List<GamePeriod> Periods { get; set; } = [];
    public List<GameGoal> Goals { get; set; } = [];
    public List<int> UnavailablePlayerIds { get; set; } = [];
}

public enum GameSplitType
{
    Halves,
    Quarters
}
