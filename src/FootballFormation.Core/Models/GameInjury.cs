namespace FootballFormation.Core.Models;

/// <summary>
/// A player hurt during a match, recorded at the moment she left the pitch for it.
/// <para>
/// Distinct from <see cref="SeasonSquadMember.IsInjured"/>, which is a standing status with no time
/// dimension: this is one afternoon, on the match clock. It is what lets a game's availability stop
/// where the injury did — see <see cref="Game.AvailableMinutesFor"/> — which a flag on the squad row
/// can never say, because it carries no date.
/// </para>
/// <para>
/// The line-up is still the source of truth for who stands where. This row says <em>when</em> she
/// stopped standing there, the same way <see cref="GameSubstitution"/> does; and when nobody came on
/// for her it is the only row that says so at all, which is why it carries the slot and position she
/// left behind.
/// </para>
/// </summary>
public class GameInjury
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int GamePeriodId { get; set; }
    public GamePeriod GamePeriod { get; set; } = null!;

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>Match-clock second she went off. Everything after it is time she could not have
    /// played.</summary>
    public int AtSeconds { get; set; }

    /// <summary>The pitch slot she left, so the record can be undone.</summary>
    public int? SlotIndex { get; set; }

    /// <summary>The position she was holding, for the same reason — and so
    /// <c>GameMinutesReport</c> can credit the minutes before it when no substitution row does.</summary>
    public PlayerPosition Position { get; set; }

    /// <summary>
    /// When it was entered. <see cref="AtSeconds"/> says where on the match clock it sits; this
    /// breaks ties against goals and substitutions in the same second. See
    /// <see cref="GameGoal.RecordedAt"/>.
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
