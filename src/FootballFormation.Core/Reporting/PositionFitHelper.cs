namespace FootballFormation.Core.Reporting;

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
    /// Broad positions (W, DEF, MID, ATT) expand to all their specific variants.
    private static readonly Dictionary<PlayerPosition, PlayerPosition[]> NaturalPositions = new()
    {

        [PlayerPosition.GK] = [],

        [PlayerPosition.DEF] = [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.RB],

        [PlayerPosition.CB] = [],
        [PlayerPosition.LB] = [],
        [PlayerPosition.RB] = [],

        [PlayerPosition.MID] = [PlayerPosition.CDM, PlayerPosition.CM, PlayerPosition.LM, PlayerPosition.RM, PlayerPosition.CAM],

        [PlayerPosition.CDM] = [PlayerPosition.CM],
        [PlayerPosition.CM]  = [PlayerPosition.CDM, PlayerPosition.CAM],
        [PlayerPosition.CAM] = [PlayerPosition.CM],
        [PlayerPosition.LM]  = [PlayerPosition.LW],
        [PlayerPosition.RM]  = [PlayerPosition.RW],

        [PlayerPosition.W] = [PlayerPosition.LW, PlayerPosition.RW],

        [PlayerPosition.LW] = [PlayerPosition.RW],
        [PlayerPosition.RW] = [PlayerPosition.LW],

        [PlayerPosition.ATT] = [PlayerPosition.ST],

        [PlayerPosition.ST] = [],
    };

    public static PositionFit GetFit(Player player, PlayerPosition slotPosition)
    {

        if (player.PreferredPosition == slotPosition)
            return PositionFit.Preferred;

        if (IsNaturalFit(player.PreferredPosition, slotPosition))
            return PositionFit.NaturalFit;

        if (player.AlternativePositions.Contains(slotPosition))
            return PositionFit.Alternative;

        if (player.AlternativePositions.Any(alt => alt == slotPosition || IsNaturalFit(alt, slotPosition)))
            return PositionFit.Compatible;

        return PositionFit.OutOfPosition;
    }

    private static bool IsNaturalFit(PlayerPosition playerPos, PlayerPosition slotPos) =>
        NaturalPositions.TryGetValue(playerPos, out var family) && family.Contains(slotPos);
}
