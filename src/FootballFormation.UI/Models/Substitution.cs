namespace FootballFormation.UI.Models;

public class Substitution
{
    public int Minute { get; set; }
    public Player PlayerOut { get; set; } = null!;
    public Player PlayerIn { get; set; } = null!;
    public string FromPosition { get; set; } = string.Empty;
    public string ToPosition { get; set; } = string.Empty;
}