namespace FootballFormation.UI.Helpers;

/// The pitch is vertical, our goal at the bottom. Slots sharing a position — two centre-backs — are spread by index across pre-defined
/// left/centre/right coordinates.
public static class PitchPositionHelper
{
    public static (double Left, double Top) GetCoordinates(PlayerPosition position, int index = 0, int count = 1) => position switch
    {

        PlayerPosition.GK => (50, 93),

        PlayerPosition.LB => count switch
        {
            1 => (10, 78),
            2 => index == 0 ? (8, 70) : (10, 78),   // wing-back + full-back spread
            _ => (10, 78)
        },
        PlayerPosition.CB => Spread(count, index, [(50, 82)], [(33, 82), (67, 82)], [(33, 82), (50, 82), (67, 82)]),
        PlayerPosition.RB => count switch
        {
            1 => (90, 78),
            2 => index == 0 ? (90, 78) : (92, 70),
            _ => (90, 78)
        },

        PlayerPosition.CDM => Spread(count, index, [(50, 65)], [(38, 65), (62, 65)]),

        PlayerPosition.CM => Spread(count, index, [(50, 53)], [(30, 55), (70, 55)], [(30, 55), (50, 53), (70, 55)]),
        PlayerPosition.LM => (8, 50),
        PlayerPosition.RM => (92, 50),

        PlayerPosition.CAM => Spread(count, index, [(50, 40)], [(35, 40), (65, 40)]),

        PlayerPosition.LW => (12, 28),
        PlayerPosition.RW => (88, 28),
        PlayerPosition.ST => Spread(count, index, [(50, 15)], [(38, 15), (62, 15)]),

        _ => (50, 50)
    };

    private static (double, double) Spread(int count, int index,
        (double, double)[] one,
        (double, double)[]? two = null,
        (double, double)[]? three = null) => count switch
    {
        1 => one[Math.Min(index, one.Length - 1)],
        2 => two is not null ? two[Math.Min(index, two.Length - 1)] : one[0],
        3 => three is not null ? three[Math.Min(index, three.Length - 1)] : two is not null ? two[Math.Min(index, two.Length - 1)] : one[0],
        _ => one[0]
    };

}
