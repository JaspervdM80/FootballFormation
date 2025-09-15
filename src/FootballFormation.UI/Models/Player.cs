using System.Text.Json.Serialization;
using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Models;

public class Player
{
    public string Name { get; set; } = string.Empty;
    public Position MainPosition { get; set; }
    public List<Position> SecondaryPositions { get; set; } = [];
    public PreferredFoot PreferredFoot { get; set; } = PreferredFoot.Right;
    public PlayerSkills Skills { get; set; } = new();
    
    // Keep these properties for backward compatibility, but they can be overridden by squad creation inputs
    [JsonIgnore]
    public bool IsKeeper { get; set; } = false;
    [JsonIgnore]
    public bool IsAbsent { get; set; } = false;
    [JsonIgnore]
    public int MinutesPlayed { get; set; } = 0;

    public double GetPositionScore(Position position)
    {
        if (position == Position.GK)
            return GetGoalkeeperScore();

        var skillScore = GetSkillScoreForPosition(position);
        var positionBonus = GetPositionBonus(position);

        return skillScore * positionBonus;
    }

    private double GetGoalkeeperScore()
    {
        if (MainPosition == Position.GK) return 10.0;
        if (SecondaryPositions.Contains(Position.GK)) return 5.0;
        return 0;
    }

    private double GetSkillScoreForPosition(Position position) => position switch
    {
        Position.DC => (Skills.Defense * 2 + Skills.Fierceness + Skills.Concentration) / 4.0,
        Position.DL or Position.DR => (Skills.Defense + Skills.Speed + Skills.Passing + Skills.Fierceness) / 4.0,
        Position.CDM => (Skills.Defense + Skills.Midfield + Skills.Passing + Skills.Vision + Skills.BallControl + Skills.Concentration) / 6.0,
        Position.CAM => (Skills.Attacking + Skills.Midfield + Skills.Passing + Skills.Vision) / 4.0,
        Position.LW or Position.RW => (Skills.Attacking + Skills.Speed + Skills.Shooting) / 3.0,
        Position.ST => (Skills.Attacking * 2 + Skills.Shooting + Skills.Speed + Skills.Fierceness) / 5.0,
        _ => 0
    };

    private double GetPositionBonus(Position position)
    {
        if (MainPosition == position) return 1.5;
        if (SecondaryPositions.Contains(position)) return 1.2;

        if (IsInSameGroup(MainPosition, position)) return 0.9;
        if (SecondaryPositions.Any(sp => IsInSameGroup(sp, position))) return 1.0;

        return 0.7;
    }

    private static bool IsInSameGroup(Position firstPosition, Position secondPosition)
    {
        return BothInGroup(firstPosition, secondPosition, Position.Defenders) ||
               BothInGroup(firstPosition, secondPosition, Position.Midfielders) ||
               BothInGroup(firstPosition, secondPosition, Position.Attackers);
    }

    private static bool BothInGroup(Position firstPosition, Position secondPosition, Position group)
    {
        return (firstPosition & group) != 0 && (secondPosition & group) != 0;
    }
}