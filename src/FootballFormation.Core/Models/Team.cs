namespace FootballFormation.Core.Models;

public class Team
{
    public int Id { get; set; }

    public int ClubId { get; set; }

    /// Null on a team read without its club — TeamService includes it on every read that hands one out.
    public Club? Club { get; set; }

    public required string Name { get; set; }

    /// Set when an admin dismisses the getting-started checklist on the home page; it never comes back once set.
    public bool SetupChecklistHidden { get; set; }

    public string FullName => Club is null ? Name : $"{Club.Name} {Name}";

    public string Slug => LinkSlug.Of(Name);
}
