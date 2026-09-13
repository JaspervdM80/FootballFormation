namespace FootballFormation.Core.Models;

public class GamePlayerPosition
{
    public int Id { get; set; }
    public int GamePeriodId { get; set; }
    public GamePeriod GamePeriod { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// Matches the formation slot for a starter; for a substitute it is her designated position.
    public PlayerPosition Position { get; set; }

    /// The source of truth for where a starter appears on the pitch: 0 is the goalkeeper, the rest outfield. Null for substitutes.
    public int? SlotIndex { get; set; }

    public bool IsSubstitute { get; set; }

    /// Her designated position comes back with her, so the bench shows what she plays rather than the slot she was taken out of.
    public void SendToBench()
    {
        IsSubstitute = true;
        Position = Player?.PreferredPosition ?? Position;
        SlotIndex = null;
    }
}
