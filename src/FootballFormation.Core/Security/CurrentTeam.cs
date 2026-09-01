namespace FootballFormation.Core.Security;

/// The remembered team while it still names one, and otherwise the first team in the database — so a visitor who has never chosen sees
/// the same app as before there was anything to choose. The host reads <paramref name="storedTeamId"/> off the ff.team cookie.
public sealed class CurrentTeam(IDbContextFactory<AppDbContext> dbFactory, int? storedTeamId) : ICurrentTeam
{
    private Task<int?>? _resolving;

    /// Memoized and shared for the same reason SeasonState's load is, which is also why it takes no cancellation token: the chrome, the
    /// page and the write guard all ask during one render, and cancelling it for one of them would answer for the others too.
    public Task<int?> GetIdAsync() => _resolving ??= ResolveAsync();

    private async Task<int?> ResolveAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var ids = await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync();

        if (storedTeamId is { } stored && ids.Contains(stored)) return stored;

        return ids.Count > 0 ? ids[0] : null;
    }
}
