namespace FootballFormation.Core.Models;

public class GameGoal
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int ScorerId { get; set; }
    public Player Scorer { get; set; } = null!;

    public int? AssisterId { get; set; }
    public Player? Assister { get; set; }

    public int? Minute { get; set; }
    public bool IsOwnGoal { get; set; }
}
