namespace FootballFormation.Core.Models;

public class Player
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public string? Surname { get; set; }
    public int? ShirtNumber { get; set; }
    public PlayerPosition PreferredPosition { get; set; }
    public List<PlayerPosition> AlternativePositions { get; set; } = [];

    public string DisplayName => Surname is not null ? $"{FirstName} {Surname}" : FirstName;
    public string ShortName => Surname is not null ? $"{FirstName[0]}. {Surname}" : FirstName;
}
