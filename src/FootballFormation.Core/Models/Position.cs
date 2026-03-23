namespace FootballFormation.Core.Models;

public enum PlayerPosition
{
    // Goalkeeper
    GK,

    // Defenders
    LB,
    LCB,
    CB,
    RCB,
    RB,
    LWB,
    RWB,
    DEF,

    // Midfielders
    LCDM,
    RCDM,
    CDM,
    LCM,
    CM,
    RCM,
    LM,
    RM,
    LCAM,
    RCAM,
    CAM,
    MID,

    // Forwards / Wingers
    LW,
    RW,
    W,
    LF,
    RF,
    CF,
    LST,
    RST,
    ST,
    ATT
}

public static class PlayerPositionExtensions
{
    public static string DisplayName(this PlayerPosition position) => position switch
    {
        PlayerPosition.GK => "Goalkeeper",
        PlayerPosition.LB => "Left Back",
        PlayerPosition.LCB => "Left Center Back",
        PlayerPosition.CB => "Center Back",
        PlayerPosition.RCB => "Right Center Back",
        PlayerPosition.RB => "Right Back",
        PlayerPosition.LWB => "Left Wing Back",
        PlayerPosition.RWB => "Right Wing Back",
        PlayerPosition.DEF => "Defender",
        PlayerPosition.LCDM => "Left Def. Midfielder",
        PlayerPosition.RCDM => "Right Def. Midfielder",
        PlayerPosition.CDM => "Defensive Midfielder",
        PlayerPosition.LCM => "Left Center Midfielder",
        PlayerPosition.CM => "Center Midfielder",
        PlayerPosition.RCM => "Right Center Midfielder",
        PlayerPosition.LM => "Left Midfielder",
        PlayerPosition.RM => "Right Midfielder",
        PlayerPosition.LCAM => "Left Att. Midfielder",
        PlayerPosition.RCAM => "Right Att. Midfielder",
        PlayerPosition.CAM => "Attacking Midfielder",
        PlayerPosition.MID => "Midfielder",
        PlayerPosition.LW => "Left Winger",
        PlayerPosition.RW => "Right Winger",
        PlayerPosition.W => "Winger",
        PlayerPosition.LF => "Left Forward",
        PlayerPosition.RF => "Right Forward",
        PlayerPosition.CF => "Center Forward",
        PlayerPosition.LST => "Left Striker",
        PlayerPosition.RST => "Right Striker",
        PlayerPosition.ST => "Striker",
        PlayerPosition.ATT => "Attacker",
        _ => position.ToString()
    };

    public static PositionCategory Category(this PlayerPosition position) => position switch
    {
        PlayerPosition.GK => PositionCategory.Goalkeeper,
        PlayerPosition.LB or PlayerPosition.LCB or PlayerPosition.CB or PlayerPosition.RCB or PlayerPosition.RB
            or PlayerPosition.LWB or PlayerPosition.RWB or PlayerPosition.DEF => PositionCategory.Defender,
        PlayerPosition.LCDM or PlayerPosition.RCDM or PlayerPosition.CDM
            or PlayerPosition.LCM or PlayerPosition.CM or PlayerPosition.RCM
            or PlayerPosition.LM or PlayerPosition.RM
            or PlayerPosition.LCAM or PlayerPosition.RCAM or PlayerPosition.CAM
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
