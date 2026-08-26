namespace FootballFormation.Core.Models;

/// Private by default, so a coach can keep working notes on the same match the public sees a report on. Visibility is enforced in
/// GameService.GetCommentsAsync, not the markup: the result page prerenders, so a body filtered out only in the UI still ships.
public class GameComment
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public required string Body { get; set; }

    /// False — admin-only — unless someone published it.
    public bool IsPublic { get; set; }

    /// Null once that account is deleted: the comment is part of the match record and outlives whoever typed it.
    public int? AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// Null until the body is changed, so an untouched comment shows no edit marker.
    public DateTime? EditedAt { get; set; }
}
