namespace FootballFormation.Core.Models;

/// <summary>
/// Something an admin wrote down about a game. Private by default: a comment is only ever visible
/// to visitors once someone deliberately publishes it, so the coach can keep working notes on the
/// same match the public sees a match report on.
/// <para>
/// Visibility is enforced in the query (<c>GameService.GetCommentsAsync</c>), not in the markup —
/// the result page prerenders server-side, so a private body filtered out only in the UI would
/// still ship to a visitor's browser.
/// </para>
/// </summary>
public class GameComment
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public required string Body { get; set; }

    /// <summary>False — admin-only — unless someone published it.</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Who wrote it. Null once that account is deleted: the comment is part of the match record and
    /// outlives whoever typed it. Only ever shown to admins.
    /// </summary>
    public int? AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null until the body is changed, so an untouched comment shows no edit marker.</summary>
    public DateTime? EditedAt { get; set; }
}
