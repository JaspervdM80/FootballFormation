namespace FootballFormation.Core.Data;

internal static class ScopeQueries
{
    /// The team gate for a write that reaches a game's child by the child's own id — a goal, a comment, an injury. The child rows carry
    /// no query filter, so this asks the filtered Games set whether the game is the scope's, turning another team's id into "not found".
    internal static Task<bool> GameInScopeAsync(
        this AppDbContext db, int gameId, CancellationToken cancellationToken) =>
        db.Games.AnyAsync(g => g.Id == gameId, cancellationToken);

    /// A game's child by its own id, or null when there is none or its game is another team's — the two read the same on purpose.
    internal static async Task<T?> FindInScopeAsync<T>(
        this AppDbContext db, int id, CancellationToken cancellationToken) where T : class, IGameChild
    {
        var row = await db.Set<T>().FindAsync([id], cancellationToken);
        return row is not null && await db.GameInScopeAsync(row.GameId, cancellationToken) ? row : null;
    }

    /// The team a row hanging off this season is stamped with. Null when the season is gone or another team's.
    internal static Task<int?> TeamIdOfSeasonAsync(
        this AppDbContext db, int seasonId, CancellationToken cancellationToken) =>
        db.Seasons
            .Where(s => s.Id == seasonId)
            .Select(s => (int?)s.TeamId)
            .FirstOrDefaultAsync(cancellationToken);
}
