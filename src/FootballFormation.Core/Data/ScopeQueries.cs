namespace FootballFormation.Core.Data;

internal static class ScopeQueries
{
    /// The team gate for a write that reaches a game's child by the child's own id — a goal, a comment, an injury. The child rows carry
    /// no query filter, so this asks the filtered Games set whether the game is the scope's, turning another team's id into "not found".
    internal static Task<bool> GameInScopeAsync(
        this AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.AnyAsync(g => g.Id == gameId, cancellationToken);

    /// The team a row hanging off this season is stamped with. Null when the season is gone or another team's.
    internal static Task<int?> TeamIdOfSeasonAsync(
        this AppDbContext db, int seasonId, CancellationToken cancellationToken) =>
        db.Seasons
            .Where(s => s.Id == seasonId)
            .Select(s => (int?)s.TeamId)
            .FirstOrDefaultAsync(cancellationToken);
}
