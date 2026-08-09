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
    /// No longer at the club. The person and every row that references them stays exactly as it
    /// was — this only takes them out of the choices for seasons still to come, so the alternative
    /// to deleting someone is not "keep them in every picker forever".
    /// <para>
    /// Deliberately not a filter on the past: an archived player still appears in the squads,
    /// lineups and statistics of the seasons they played, because they did play them.
    /// </para>
    /// </summary>
    public bool IsArchived { get; set; }

    // Guest status is deliberately NOT here: it belongs to a season's squad, not to the person.
    // See SeasonSquadMember.IsGuest.

    public string DisplayName => Surname is not null ? $"{FirstName} {Surname}" : FirstName;
    public string ShortName => Surname is not null ? $"{FirstName[0]}. {Surname}" : FirstName;
}
