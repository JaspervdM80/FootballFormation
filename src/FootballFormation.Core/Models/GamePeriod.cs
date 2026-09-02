namespace FootballFormation.Core.Models;

/// The match is only ever two halves, so the row opening one carries its timings and everything the live screen records — a row planned
/// for the middle of a half stays a plan and is never kicked off.
public class GamePeriod
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;
    public PeriodType PeriodType { get; set; }
    public FormationType? FormationTypeOverride { get; set; }

    /// Null for a line-up never run live — a mid-half plan, or a game never played from the touchline.
    public int? StartedAtSeconds { get; set; }

    /// Past this point the touchline owns the line-up: GameService.SavePeriodLineupAsync refuses to replace it.
    public bool HasKickedOff => StartedAtSeconds is not null;

    /// Null while the half is still running.
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

    /// Quarters are a planning device, so anything shown to someone watching goes through here rather than naming the quarter.
    public static PeriodType Half(this PeriodType period) => period switch
    {
        PeriodType.FirstHalf or PeriodType.FirstQuarter or PeriodType.SecondQuarter => PeriodType.FirstHalf,
        _ => PeriodType.SecondHalf
    };

    /// "1st Half" / "2nd Half", whichever period this actually is.
    public static string HalfDisplayName(this PeriodType period) => period.Half().DisplayName();

    public static PeriodType[] ForSplitType(GameSplitType splitType) => splitType switch
    {
        GameSplitType.Halves => [PeriodType.FirstHalf, PeriodType.SecondHalf],
        GameSplitType.Quarters => [PeriodType.FirstQuarter, PeriodType.SecondQuarter, PeriodType.ThirdQuarter, PeriodType.FourthQuarter],
        _ => []
    };
}
