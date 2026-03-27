namespace FootballFormation.Core.Models;

public enum PlayerPosition
{
    // Goalkeeper
    GK = 0,

    // Defenders
    LB = 1,
    CB = 3,
    RB = 5,
    DEF = 8,

    // Midfielders
    CDM = 11,
    CM = 13,
    LM = 15,
    RM = 16,
    CAM = 19,
    MID = 20,

    // Forwards / Wingers
    LW = 21,
    RW = 22,
    W = 23,
    ST = 29,
    ATT = 30
}

public static class PlayerPositionExtensions
{
    public static string DisplayName(this PlayerPosition position) => position switch
    {
        PlayerPosition.GK => "Goalkeeper",
        PlayerPosition.LB => "Left Back",
        PlayerPosition.CB => "Center Back",
        PlayerPosition.RB => "Right Back",
        PlayerPosition.DEF => "Defender",
        PlayerPosition.CDM => "Defensive Midfielder",
        PlayerPosition.CM => "Center Midfielder",
        PlayerPosition.LM => "Left Midfielder",
        PlayerPosition.RM => "Right Midfielder",
        PlayerPosition.CAM => "Attacking Midfielder",
        PlayerPosition.MID => "Midfielder",
        PlayerPosition.LW => "Left Winger",
        PlayerPosition.RW => "Right Winger",
        PlayerPosition.W => "Winger",
        PlayerPosition.ST => "Striker",
        PlayerPosition.ATT => "Attacker",
        _ => position.ToString()
    };

    public static PositionCategory Category(this PlayerPosition position) => position switch
    {
        PlayerPosition.GK => PositionCategory.Goalkeeper,
        PlayerPosition.LB or PlayerPosition.CB or PlayerPosition.RB
            or PlayerPosition.DEF => PositionCategory.Defender,
        PlayerPosition.CDM or PlayerPosition.CM
            or PlayerPosition.LM or PlayerPosition.RM
            or PlayerPosition.CAM
            or PlayerPosition.MID => PositionCategory.Midfielder,
        _ => PositionCategory.Forward
    };
}

public enum PositionCategory
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward
}
