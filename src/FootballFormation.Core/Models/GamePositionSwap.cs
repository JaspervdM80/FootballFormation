namespace FootballFormation.Core.Models;

/// Two players on the pitch trading places from <see cref="AtSeconds"/> until a later change moves either. Nobody's minutes change, but
/// without the second the split by position cannot say who stood where before it.
public class GamePositionSwap
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int GamePeriodId { get; set; }
    public int PlayerAId { get; set; }
    public int PlayerBId { get; set; }
    public int AtSeconds { get; set; }

    /// Breaks ties against goals and substitutions in the same second. See <see cref="GameGoal.RecordedAt"/>.
    public DateTime RecordedAt { get; set; }
}
