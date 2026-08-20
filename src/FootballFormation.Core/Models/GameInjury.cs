namespace FootballFormation.Core.Models;

/// <summary>
/// A player hurt during a match, stamped with the moment she left the pitch.
/// <para>
/// Distinct from <see cref="SeasonSquadMember.IsInjured"/>, which is a standing status with no date
/// on it. Only a moment on the clock can stop a game's availability where the injury did — see
/// <see cref="Game.AvailableMinutesFor"/>. When nobody came on for her this is also the only row
/// that says she left at all, which is why it carries the slot and position she left behind.
/// </para>
/// <para>
/// Foreign keys with no navigation beside them, the way <see cref="GameGoal.GamePeriodId"/> is
/// written: the half is resolved against the game's own <see cref="Game.Periods"/> and the player
/// against the pool the live screen already loads, so a navigation would only invite a second
/// <c>Include</c> of the same rows.
/// </para>
/// </summary>
public class GameInjury
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int GamePeriodId { get; set; }
    public int PlayerId { get; set; }

    /// <summary>Match-clock second she went off. Everything after it is time she could not have
    /// played.</summary>
    public int AtSeconds { get; set; }

    public int? SlotIndex { get; set; }
    public PlayerPosition Position { get; set; }

    /// <summary>UTC entry time — breaks ties against goals and substitutions in the same second.
    /// See <see cref="GameGoal.RecordedAt"/>.</summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
