namespace FootballFormation.Models;

public class PlayerSkills
{
    public int Attacking { get; set; } = 1;
    public int Midfield { get; set; } = 1;
    public int Defense { get; set; } = 1;
    public int Passing { get; set; } = 1;
    public int Speed { get; set; } = 1;
    public int Shooting { get; set; } = 1;
    public int Insight { get; set; } = 1;
    public int Fierceness { get; set; } = 1;

    public double AverageSkill =>
        (Attacking + Midfield + Defense + Passing + Speed + Shooting + Insight + Fierceness) / 8.0;
}