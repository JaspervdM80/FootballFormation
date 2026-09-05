namespace FootballFormation.Core.Security;

/// The remembered team while it still names one, and otherwise the first team in the database — so a visitor who has never chosen sees
/// the same app as before there was anything to choose. The host reads <paramref name="storedTeamId"/> off the ff.team cookie.
public sealed class CurrentTeam(IRawDbContextFactory dbFactory, int? storedTeamId) : ICurrentTeam
{
    private Task<Resolved>? _resolving;

    private readonly record struct Resolved(int? TeamId, int? ClubId);

    /// Memoized and shared for the same reason SeasonState's load is, which is also why it takes no cancellation token: the chrome, the
    /// page and the write guard all ask during one render, and cancelling it for one of them would answer for the others too. Resolved off
    /// the raw factory, never the team-scoped one, so stamping a context does not ask the context which team it is.
    public async Task<int?> GetIdAsync() => (await Resolve()).TeamId;

    public async Task<int?> GetClubIdAsync() => (await Resolve()).ClubId;

    private Task<Resolved> Resolve() => _resolving ??= ResolveAsync();

    private async Task<Resolved> ResolveAsync()
    {
        await using var db = dbFactory.CreateDbContext();

        var teams = await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.ClubId })
            .ToListAsync();

        var team = storedTeamId is { } stored
            ? teams.FirstOrDefault(t => t.Id == stored) ?? teams.FirstOrDefault()
            : teams.FirstOrDefault();

        return team is null ? new Resolved(null, null) : new Resolved(team.Id, team.ClubId);
    }
}
