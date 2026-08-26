namespace FootballFormation.Core.Models;

public class Player
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public string? Surname { get; set; }
    public int? ShirtNumber { get; set; }
    public PlayerPosition PreferredPosition { get; set; }
    public List<PlayerPosition> AlternativePositions { get; set; } = [];

    /// Takes someone out of the pickers for seasons still to come and nothing else: an archived player still appears in the squads,
    /// line-ups and statistics of the seasons she played, because she did play them.
    public bool IsArchived { get; set; }

    // Guest status is deliberately not here — it belongs to a season's squad, not to the person. See SeasonSquadMember.IsGuest.

    public string DisplayName => Surname is not null ? $"{FirstName} {Surname}" : FirstName;
    public string ShortName => Surname is not null ? $"{FirstName[0]}. {Surname}" : FirstName;
}
