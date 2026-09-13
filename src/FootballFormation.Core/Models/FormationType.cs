namespace FootballFormation.Core.Models;

public enum FormationType
{
    F442,
    F433,
    F4231,
    F352,
    F343,
    F4141,
    F4411,
    F532,
    F541,
    F4321,
    F3421,
    F3511,
    F442Diamond,

    // Nine-a-side, appended rather than filed beside the shapes they resemble: the numbers are in the database.
    F332,
    F323,
    F233,
    F242
}

public static class FormationTypeExtensions
{
    private static readonly Dictionary<MatchFormat, IReadOnlyList<FormationType>> ByFormat =
        Enum.GetValues<FormationType>()
            .GroupBy(f => f.Format())
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FormationType>)[.. group.OrderBy(f => f.DisplayName(), StringComparer.Ordinal)]);

    /// Every picker offers one format's shapes in this order — the enum's own order is the order they were added, which is no order at
    /// all to hunt through.
    public static IReadOnlyList<FormationType> Alphabetical(MatchFormat format) =>
        ByFormat.GetValueOrDefault(format, []);

    /// Derived from the shape itself rather than recorded beside it, so a game can never claim one format and field another.
    public static MatchFormat Format(this FormationType formation) =>
        (MatchFormat)(formation.DefaultPositions().Length + 1);

    public static string DisplayName(this FormationType formation) => formation switch
    {
        FormationType.F442 => "4-4-2",
        FormationType.F433 => "4-3-3",
        FormationType.F4231 => "4-2-3-1",
        FormationType.F352 => "3-5-2",
        FormationType.F343 => "3-4-3",
        FormationType.F4141 => "4-1-4-1",
        FormationType.F4411 => "4-4-1-1",
        FormationType.F532 => "5-3-2",
        FormationType.F541 => "5-4-1",
        FormationType.F4321 => "4-3-2-1",
        FormationType.F3421 => "3-4-2-1",
        FormationType.F3511 => "3-5-1-1",
        FormationType.F442Diamond => "4-4-2 diamond",
        FormationType.F332 => "3-3-2",
        FormationType.F323 => "3-2-3",
        FormationType.F233 => "2-3-3",
        FormationType.F242 => "2-4-2",
        _ => formation.ToString()
    };

    public static PlayerPosition[] DefaultPositions(this FormationType formation) => formation switch
    {
        FormationType.F442 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST, PlayerPosition.ST],
        FormationType.F433 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.LW, PlayerPosition.ST, PlayerPosition.RW],
        FormationType.F4231 => [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CDM, PlayerPosition.CDM, PlayerPosition.LW, PlayerPosition.CAM, PlayerPosition.RW, PlayerPosition.ST],
        FormationType.F352 =>  [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST, PlayerPosition.ST],
        FormationType.F343 =>  [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.LW, PlayerPosition.ST, PlayerPosition.RW],
        FormationType.F4141 => [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CDM, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST],
        FormationType.F4411 => [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.CAM, PlayerPosition.ST],
        FormationType.F532 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.ST, PlayerPosition.ST],
        FormationType.F541 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST],
        FormationType.F4321 => [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.LW, PlayerPosition.RW, PlayerPosition.ST],
        FormationType.F3421 => [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.LW, PlayerPosition.RW, PlayerPosition.ST],
        FormationType.F3511 => [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.CAM, PlayerPosition.ST],
        FormationType.F442Diamond => [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CDM, PlayerPosition.LM, PlayerPosition.RM, PlayerPosition.CAM, PlayerPosition.ST, PlayerPosition.ST],
        FormationType.F332 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST, PlayerPosition.ST],
        FormationType.F323 =>  [PlayerPosition.LB, PlayerPosition.CB, PlayerPosition.RB, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.LW, PlayerPosition.ST, PlayerPosition.RW],
        FormationType.F233 =>  [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.LW, PlayerPosition.ST, PlayerPosition.RW],
        FormationType.F242 =>  [PlayerPosition.CB, PlayerPosition.CB, PlayerPosition.LM, PlayerPosition.CM, PlayerPosition.CM, PlayerPosition.RM, PlayerPosition.ST, PlayerPosition.ST],
        _ => []
    };
}
