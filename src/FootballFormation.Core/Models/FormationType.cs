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
    F3511
}

public static class FormationTypeExtensions
{
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
        _ => []
    };
}
