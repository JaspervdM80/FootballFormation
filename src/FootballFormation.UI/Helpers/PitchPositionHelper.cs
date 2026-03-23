using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>
/// Maps positions to (left%, top%) coordinates on the pitch.
/// Pitch is vertical: our goal at bottom, opponent goal at top.
/// </summary>
public static class PitchPositionHelper
{
    public static (double Left, double Top) GetCoordinates(PlayerPosition position) => position switch
    {
        // Goalkeeper
        PlayerPosition.GK => (50, 93),

        // Defenders
        PlayerPosition.LB => (10, 78),
        PlayerPosition.LCB => (33, 82),
        PlayerPosition.CB => (50, 82),
        PlayerPosition.RCB => (67, 82),
        PlayerPosition.RB => (90, 78),
        PlayerPosition.LWB => (8, 70),
        PlayerPosition.RWB => (92, 70),

        // Defensive midfielders
        PlayerPosition.CDM => (50, 65),
        PlayerPosition.LCDM => (38, 65),
        PlayerPosition.RCDM => (62, 65),

        // Central midfielders
        PlayerPosition.LCM => (30, 55),
        PlayerPosition.CM => (50, 53),
        PlayerPosition.RCM => (70, 55),
        PlayerPosition.LM => (8, 50),
        PlayerPosition.RM => (92, 50),

        // Attacking midfielders
        PlayerPosition.CAM => (50, 40),
        PlayerPosition.LCAM => (35, 40),
        PlayerPosition.RCAM => (65, 40),

        // Forwards / Wingers
        PlayerPosition.LW => (12, 28),
        PlayerPosition.RW => (88, 28),
        PlayerPosition.LF => (33, 25),
        PlayerPosition.RF => (67, 25),
        PlayerPosition.CF => (50, 20),
        PlayerPosition.LST => (38, 15),
        PlayerPosition.RST => (62, 15),
        PlayerPosition.ST => (50, 15),

        _ => (50, 50)
    };
}
