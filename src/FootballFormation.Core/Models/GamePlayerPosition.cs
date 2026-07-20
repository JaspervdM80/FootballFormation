namespace FootballFormation.Core.Models;

public class GamePlayerPosition
{
    public int Id { get; set; }
    public int GamePeriodId { get; set; }
    public GamePeriod GamePeriod { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>
    /// The position enum value. For starters this matches the formation slot;
    /// for substitutes it's their designated position.
    /// </summary>
    public PlayerPosition Position { get; set; }

    /// <summary>
    /// The formation slot index (0 = GK, 1-10 = outfield).
    /// This is the source of truth for where a starter appears on the pitch.
    /// Null for substitutes.
    /// </summary>
    public int? SlotIndex { get; set; }

    public bool IsSubstitute { get; set; }
}
