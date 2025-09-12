namespace FootballFormation.Models;

public class Formation
{
    public string Period { get; set; } = string.Empty;
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public Dictionary<string, Player?> PositionedPlayers { get; set; } = new();
    public List<Player> Bench { get; set; } = new();
    public Player? Goalkeeper { get; set; }

    public double TeamStrength
    {
        get
        {
            var players = PositionedPlayers.Values.Where(p => p != null).ToList();
            if (Goalkeeper != null) players.Add(Goalkeeper);
            return players.Any() ? players.Average(p => p!.Skills.AverageSkill) : 0;
        }
    }
}
