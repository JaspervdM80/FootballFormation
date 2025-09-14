namespace FootballFormation.UI.Models;

public class Squad
{
    public Player Goalkeeper { get; set; } = null!;
    public List<Player> FieldPlayers { get; set; } = [];
    public int HalfNumber { get; set; } // 1 or 2
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}
