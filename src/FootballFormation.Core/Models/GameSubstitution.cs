namespace FootballFormation.Core.Models;

/// <summary>
/// One change made during a live match. The lineup itself is still the source of truth for who
/// stands where (see <see cref="GamePlayerPosition"/>); this records <em>when</em> the swap
/// happened, which the period lineup alone cannot express.
/// </summary>
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

    /// <summary>Match-clock second the change was made.</summary>
    public int AtSeconds { get; set; }

    /// <summary>The pitch slot that changed hands, so the swap can be reversed.</summary>
    public int? SlotIndex { get; set; }

    /// <summary>The position that changed hands, for the same reason.</summary>
    public PlayerPosition Position { get; set; }

    /// <summary>
    /// When the change was entered. <see cref="AtSeconds"/> says where on the match clock it sits;
    /// this breaks ties against goals in the same minute. See <see cref="GameGoal.RecordedAt"/>.
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
