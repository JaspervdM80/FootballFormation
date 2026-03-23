using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

public enum PositionFit
{
    Preferred,      // Exact preferred position match
    NaturalFit,     // Preferred position naturally covers this slot (e.g. W → LW, DEF → CB)
    Alternative,    // Explicitly listed as alternative position
    Compatible,     // An alternative position naturally covers this slot
    OutOfPosition   // No relationship at all
}

public static class PositionFitHelper
{
    /// <summary>
    /// Maps each position to the specific slots it naturally covers.
    /// Broad positions (W, DEF, MID, ATT) expand to all their specific variants.
    /// Specific positions map to their close variants (CB ↔ LCB/RCB, ST ↔ LST/RST, etc.).
    /// </summary>
    private static readonly Dictionary<PlayerPosition, PlayerPosition[]> NaturalPositions = new()
    {
        // Goalkeeper
        [PlayerPosition.GK] = [],

        // Defenders — broad
        [PlayerPosition.DEF] = [PlayerPosition.LB, PlayerPosition.LCB, PlayerPosition.CB, PlayerPosition.RCB, PlayerPosition.RB, PlayerPosition.LWB, PlayerPosition.RWB],

        // Defenders — specific (close variants only)
        [PlayerPosition.CB]  = [PlayerPosition.LCB, PlayerPosition.RCB],
        [PlayerPosition.LCB] = [PlayerPosition.CB, PlayerPosition.RCB],
        [PlayerPosition.RCB] = [PlayerPosition.CB, PlayerPosition.LCB],
        [PlayerPosition.LB]  = [PlayerPosition.LWB],
        [PlayerPosition.RB]  = [PlayerPosition.RWB],
        [PlayerPosition.LWB] = [PlayerPosition.LB],
        [PlayerPosition.RWB] = [PlayerPosition.RB],

        // Midfielders — broad
        [PlayerPosition.MID] = [PlayerPosition.LCDM, PlayerPosition.RCDM, PlayerPosition.CDM, PlayerPosition.LCM, PlayerPosition.CM, PlayerPosition.RCM, PlayerPosition.LM, PlayerPosition.RM, PlayerPosition.LCAM, PlayerPosition.RCAM, PlayerPosition.CAM],

        // Midfielders — specific
        [PlayerPosition.CDM]  = [PlayerPosition.LCDM, PlayerPosition.RCDM],
        [PlayerPosition.LCDM] = [PlayerPosition.CDM, PlayerPosition.RCDM],
        [PlayerPosition.RCDM] = [PlayerPosition.CDM, PlayerPosition.LCDM],
        [PlayerPosition.CM]   = [PlayerPosition.LCM, PlayerPosition.RCM],
        [PlayerPosition.LCM]  = [PlayerPosition.CM, PlayerPosition.RCM],
        [PlayerPosition.RCM]  = [PlayerPosition.CM, PlayerPosition.LCM],
        [PlayerPosition.CAM]  = [PlayerPosition.LCAM, PlayerPosition.RCAM],
        [PlayerPosition.LCAM] = [PlayerPosition.CAM, PlayerPosition.RCAM],
        [PlayerPosition.RCAM] = [PlayerPosition.CAM, PlayerPosition.LCAM],
        [PlayerPosition.LM]   = [PlayerPosition.LW],
        [PlayerPosition.RM]   = [PlayerPosition.RW],

        // Wingers — broad
        [PlayerPosition.W] = [PlayerPosition.LW, PlayerPosition.RW, PlayerPosition.LF, PlayerPosition.RF],

        // Wingers — specific
        [PlayerPosition.LW] = [PlayerPosition.RW, PlayerPosition.LF],
        [PlayerPosition.RW] = [PlayerPosition.LW, PlayerPosition.RF],

        // Forwards — broad
        [PlayerPosition.ATT] = [PlayerPosition.ST, PlayerPosition.LST, PlayerPosition.RST, PlayerPosition.CF, PlayerPosition.LF, PlayerPosition.RF],

        // Forwards — specific
        [PlayerPosition.ST]  = [PlayerPosition.LST, PlayerPosition.RST, PlayerPosition.CF],
        [PlayerPosition.LST] = [PlayerPosition.ST, PlayerPosition.RST, PlayerPosition.CF],
        [PlayerPosition.RST] = [PlayerPosition.ST, PlayerPosition.LST, PlayerPosition.CF],
        [PlayerPosition.CF]  = [PlayerPosition.ST, PlayerPosition.LST, PlayerPosition.RST],
        [PlayerPosition.LF]  = [PlayerPosition.RF, PlayerPosition.LW],
        [PlayerPosition.RF]  = [PlayerPosition.LF, PlayerPosition.RW],
    };

    public static PositionFit GetFit(Player player, PlayerPosition slotPosition)
    {
        // Tier 1: exact preferred
        if (player.PreferredPosition == slotPosition)
            return PositionFit.Preferred;

        // Tier 2: preferred position naturally covers slot
        if (IsNaturalFit(player.PreferredPosition, slotPosition))
            return PositionFit.NaturalFit;

        // Tier 3: exact alternative match
        if (player.AlternativePositions.Contains(slotPosition))
            return PositionFit.Alternative;

        // Tier 4: an alternative position naturally covers slot
        if (player.AlternativePositions.Any(alt => alt == slotPosition || IsNaturalFit(alt, slotPosition)))
            return PositionFit.Compatible;

        return PositionFit.OutOfPosition;
    }

    private static bool IsNaturalFit(PlayerPosition playerPos, PlayerPosition slotPos)
    {
        if (NaturalPositions.TryGetValue(playerPos, out var family))
            return family.Contains(slotPos);

        return false;
    }

    public static string GetFitMarker(Player player, PlayerPosition position)
    {
        return GetFit(player, position) switch
        {
            PositionFit.Preferred => "***",
            PositionFit.NaturalFit => "**½",
            PositionFit.Alternative => "**",
            PositionFit.Compatible => "*",
            _ => ""
        };
    }
}
