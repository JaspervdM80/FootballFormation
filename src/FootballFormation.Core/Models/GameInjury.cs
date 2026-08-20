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
/// <para>
/// Three foreign keys and no navigation beside any of them, the way
/// <see cref="GameGoal.GamePeriodId"/> is written and for the same reason: every reader already has
/// what it needs. The half is resolved against the game's own <see cref="Game.Periods"/>
/// (<c>MatchClockReport</c>), and the player against the pool the live screen loads once for the
/// whole match — so a navigation would only be an invitation to <c>Include</c> the same rows again.
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
