namespace FootballFormation.Core.Models;

public class Player
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public string? Surname { get; set; }
    public int? ShirtNumber { get; set; }
    public PlayerPosition PreferredPosition { get; set; }
    public List<PlayerPosition> AlternativePositions { get; set; } = [];

    /// <summary>
    /// Guest players are not part of the regular squad. They are excluded from a game
    /// unless explicitly listed in <see cref="Game.GuestPlayerIds"/>.
    /// </summary>
    public bool IsGuest { get; set; }

    public string DisplayName => Surname is not null ? $"{FirstName} {Surname}" : FirstName;
    public string ShortName => Surname is not null ? $"{FirstName[0]}. {Surname}" : FirstName;
}
