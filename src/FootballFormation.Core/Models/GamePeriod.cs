namespace FootballFormation.Core.Models;

public class GamePeriod
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;
    public PeriodType PeriodType { get; set; }
    public FormationType? FormationTypeOverride { get; set; }

    /// <summary>
    /// Match-clock second this period kicked off, set by the live match screen. Null for periods
    /// that were never run live — the lineup builder does not need it.
    /// </summary>
    public int? StartedAtSeconds { get; set; }

    /// <summary>Match-clock second this period was whistled off. Null while it is still running.</summary>
    public int? EndedAtSeconds { get; set; }

    public List<GamePlayerPosition> PlayerPositions { get; set; } = [];
}

public enum PeriodType
{
    FirstHalf,
    SecondHalf,
    FirstQuarter,
    SecondQuarter,
    ThirdQuarter,
    FourthQuarter
}

public static class PeriodTypeExtensions
{
    public static string DisplayName(this PeriodType period) => period switch
    {
        PeriodType.FirstHalf => "1st Half",
        PeriodType.SecondHalf => "2nd Half",
        PeriodType.FirstQuarter => "Q1",
        PeriodType.SecondQuarter => "Q2",
        PeriodType.ThirdQuarter => "Q3",
        PeriodType.FourthQuarter => "Q4",
        _ => period.ToString()
    };

    /// <summary>
    /// Whether play actually stops after this period. A quarters game is still two halves: the
    /// teams roll straight from Q1 into Q2 and from Q3 into Q4 without leaving the pitch, and the
    /// only real break is half time. This is what stops the live screen offering a whistle after
    /// every quarter.
    /// </summary>
    public static bool IsFollowedByBreak(this PeriodType period) =>
        period is PeriodType.FirstHalf or PeriodType.SecondQuarter;

    public static PeriodType[] ForSplitType(GameSplitType splitType) => splitType switch
    {
        GameSplitType.Halves => [PeriodType.FirstHalf, PeriodType.SecondHalf],
        GameSplitType.Quarters => [PeriodType.FirstQuarter, PeriodType.SecondQuarter, PeriodType.ThirdQuarter, PeriodType.FourthQuarter],
        _ => []
    };
}
