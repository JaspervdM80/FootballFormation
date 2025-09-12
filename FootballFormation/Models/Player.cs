using FootballFormation.Enums;

namespace FootballFormation.Models;

public class Player
{
    public string Name { get; set; } = string.Empty;
    public List<Position> PreferredPositions { get; set; } = new();
    public int OverallRating { get; set; }
    public PlayerSkills Skills { get; set; } = new();
    public string Qualities { get; set; } = string.Empty;
    public bool IsKeeper { get; set; }
    public bool IsAbsent { get; set; }
    public int MinutesPlayed { get; set; }

    // Calculate position suitability based on skills
    public double GetPositionScore(Position position)
    {
        return position switch
        {
            Position.GK => IsKeeper ? 10 : 0,
            Position.CB => (Skills.Defense * 2 + Skills.Fierceness + Skills.Passing) / 4.0,
            Position.LB or Position.RB => (Skills.Defense + Skills.Speed + Skills.Passing + Skills.Fierceness) / 4.0,
            Position.CDM => (Skills.Defense + Skills.Midfield + Skills.Passing + Skills.Insight) / 4.0,
            Position.CAM => (Skills.Attacking + Skills.Midfield + Skills.Passing + Skills.Insight) / 4.0,
            Position.LW or Position.RW => (Skills.Attacking + Skills.Speed + Skills.Shooting) / 3.0,
            Position.ST => (Skills.Attacking + Skills.Shooting + Skills.Speed) / 3.0,
            _ => 0
        };
    }
}