namespace FootballFormation.Core.Models;

/// Membership is per season on purpose: someone can be a guest in 2025/26 and a full squad player in 2026/27.
public class SeasonSquadMember
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// Guests are out of every game unless listed in <see cref="Game.GuestPlayerIds"/>; full members are in unless marked unavailable.
    public bool IsGuest { get; set; }

    /// Standing, as opposed to <see cref="Game.UnavailablePlayerIds"/>, which opts someone out of a single fixture. Undated, so every
    /// match she misses copies it into its own <see cref="Game.InjuredPlayerIds"/> as it settles — and CopyFromAsync does not carry it.
    public bool IsInjured { get; set; }
}
