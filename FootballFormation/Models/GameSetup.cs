namespace FootballFormation.Models;

public class GameSetup
{
    public int SetupId { get; set; }
    public List<Formation> Formations { get; set; } = [];
    public List<Substitution> Substitutions { get; set; } = [];
    public double TotalTeamStrength { get; set; }
    public int PlayingTimeVariance { get; set; } // Difference between max and min playing time
    public double PlayingTimeFairness { get; set; } // Lower is better (standard deviation of playing time)
}
