namespace FootballFormation.Core.Models;

/// Distinct from <see cref="SeasonSquadMember.IsInjured"/>, which has no date on it: only a moment on the clock can stop a game's
/// availability where the injury did. When nobody came on for her this is the only row saying she left, hence the slot and position.
public class GameInjury
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int GamePeriodId { get; set; }
    public int PlayerId { get; set; }

    /// Match-clock second she went off. Everything after it is time she could not have played.
    public int AtSeconds { get; set; }

    public int? SlotIndex { get; set; }
    public PlayerPosition Position { get; set; }

    /// Breaks ties against goals and substitutions in the same second. See <see cref="GameGoal.RecordedAt"/>.
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
