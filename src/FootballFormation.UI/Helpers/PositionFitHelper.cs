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
    /// </summary>
    private static readonly Dictionary<PlayerPosition, PlayerPosition[]> NaturalPositions = new()
    {
        // Goalkeeper
        [PlayerPosition.GK] = [],

        // Defenders — broad
        [PlayerPosition.DEF] = [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.RB],

        // Defenders — specific
        [PlayerPosition.CB] = [],
        [PlayerPosition.LB] = [],
        [PlayerPosition.RB] = [],

        // Midfielders — broad
        [PlayerPosition.MID] = [PlayerPosition.CDM, PlayerPosition.CM, PlayerPosition.LM, PlayerPosition.RM, PlayerPosition.CAM],

        // Midfielders — specific
        [PlayerPosition.CDM] = [PlayerPosition.CM],
        [PlayerPosition.CM]  = [PlayerPosition.CDM, PlayerPosition.CAM],
        [PlayerPosition.CAM] = [PlayerPosition.CM],
        [PlayerPosition.LM]  = [PlayerPosition.LW],
        [PlayerPosition.RM]  = [PlayerPosition.RW],

        // Wingers — broad
        [PlayerPosition.W] = [PlayerPosition.LW, PlayerPosition.RW],

        // Wingers — specific
        [PlayerPosition.LW] = [PlayerPosition.RW],
        [PlayerPosition.RW] = [PlayerPosition.LW],

        // Forwards — broad
        [PlayerPosition.ATT] = [PlayerPosition.ST],

        // Forwards — specific
        [PlayerPosition.ST] = [],
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

    private static bool IsNaturalFit(PlayerPosition playerPos, PlayerPosition slotPos) =>
        NaturalPositions.TryGetValue(playerPos, out var family) && family.Contains(slotPos);
}
