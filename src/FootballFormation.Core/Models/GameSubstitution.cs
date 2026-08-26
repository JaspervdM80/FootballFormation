namespace FootballFormation.Core.Models;

/// The line-up stays the source of truth for who stands where; this records when the swap happened, which it alone cannot express.
public class GameSubstitution
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int GamePeriodId { get; set; }
    public GamePeriod GamePeriod { get; set; } = null!;

    public int PlayerOffId { get; set; }
    public Player PlayerOff { get; set; } = null!;

    public int PlayerOnId { get; set; }
    public Player PlayerOn { get; set; } = null!;

    /// Match-clock second the change was made.
    public int AtSeconds { get; set; }

    /// The pitch slot that changed hands, so the swap can be reversed.
    public int? SlotIndex { get; set; }

    /// The position that changed hands, for the same reason.
    public PlayerPosition Position { get; set; }

    /// Breaks ties against goals in the same second, where <see cref="AtSeconds"/> cannot. See <see cref="GameGoal.RecordedAt"/>.
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
