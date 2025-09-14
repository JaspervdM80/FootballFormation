namespace FootballFormation.Models;

public class PlayerPlayingTime
{
    public Player Player { get; set; } = null!;
    public int TargetMinutes { get; set; }
    public int ActualMinutes { get; set; }
    public int MinutesDeficit => TargetMinutes - ActualMinutes;
    public bool NeedsMoreTime => MinutesDeficit > 0;
    public double Priority { get; set; } // Higher = should be selected first
}