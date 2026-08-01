namespace FootballFormation.Core.Models;

public class GameGoal
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    /// <summary>Null for an opponent goal — we don't track their players.</summary>
    public int? ScorerId { get; set; }
    public Player? Scorer { get; set; }

    public int? AssisterId { get; set; }
    public Player? Assister { get; set; }

    public int? Minute { get; set; }

    /// <summary>One of ours put it in our own net. Counts for the opponent.</summary>
    public bool IsOwnGoal { get; set; }

    /// <summary>The opponent scored. Counts for the opponent, and has no scorer.</summary>
    public bool IsOpponentGoal { get; set; }
}
