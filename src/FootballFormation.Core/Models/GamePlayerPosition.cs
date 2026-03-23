namespace FootballFormation.Core.Models;

public class GamePlayerPosition
{
    public int Id { get; set; }
    public int GamePeriodId { get; set; }
    public GamePeriod GamePeriod { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public PlayerPosition Position { get; set; }
    public bool IsSubstitute { get; set; }
}
