namespace FootballFormation.Core.Models;

public class GamePeriod
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;
    public PeriodType PeriodType { get; set; }
    public FormationType? FormationTypeOverride { get; set; }

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

    public static PeriodType[] ForSplitType(GameSplitType splitType) => splitType switch
    {
        GameSplitType.Halves => [PeriodType.FirstHalf, PeriodType.SecondHalf],
        GameSplitType.Quarters => [PeriodType.FirstQuarter, PeriodType.SecondQuarter, PeriodType.ThirdQuarter, PeriodType.FourthQuarter],
        _ => []
    };
}
