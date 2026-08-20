namespace FootballFormation.Core.Models;

/// <summary>
/// A player's membership of one season's squad. The squad is authoritative: it decides who can be
/// picked for that season's games and who appears in its stats.
/// <para>
/// <see cref="IsGuest"/> lives here rather than on <see cref="Player"/> on purpose — someone can be
/// a guest in 2025/26 and a full squad player in 2026/27.
/// </para>
/// </summary>
public class SeasonSquadMember
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>Guests are left out of every game in this season unless explicitly listed in
    /// <see cref="Game.GuestPlayerIds"/>. Full members are in unless marked unavailable.</summary>
    public bool IsGuest { get; set; }

    /// <summary>
    /// Generally injured, as opposed to <see cref="Game.UnavailablePlayerIds"/>, which opts someone
    /// out of a single fixture. She is offered no place in a line-up while it is set, and every
    /// match she misses copies it into its own <see cref="Game.InjuredPlayerIds"/> as it settles.
    /// <para>
    /// Lives here rather than on <see cref="Player"/> for the same reason as <see cref="IsGuest"/>:
    /// it is this season's medical status, not the person's. It is also why
    /// <c>SeasonSquadService.CopyFromAsync</c> does not carry it forward — an injury from last
    /// season has usually healed by the time next season's squad is copied.
    /// </para>
    /// </summary>
    public bool IsInjured { get; set; }
}
