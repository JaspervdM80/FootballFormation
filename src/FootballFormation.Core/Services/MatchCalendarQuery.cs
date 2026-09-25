namespace FootballFormation.Core.Services;

public sealed record MatchCalendar(string TeamFullName, IReadOnlyList<Game> Games);

/// A calendar app polls without the team cookie, so the team comes from the URL or the game itself rather than ICurrentTeam, which would
/// answer "the first team" for every subscriber. Stamped onto the context the way MatchAudienceQuery does it.
public sealed class MatchCalendarQuery(IRawDbContextFactory dbFactory)
{
    public async Task<MatchCalendar?> ForTeamAsync(int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = dbFactory.CreateDbContext();

        var team = await db.Teams
            .AsNoTracking()
            .Include(t => t.Club)
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);

        if (team is null) return null;

        db.CurrentTeamId = team.Id;
        db.CurrentClubId = team.ClubId;

        var games = await db.Games.AsNoTracking().ToListAsync(cancellationToken);

        return new MatchCalendar(team.FullName, games.OldestFirst());
    }

    public async Task<MatchCalendar?> ForGameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = dbFactory.CreateDbContext();

        var game = await db.Games
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

        if (game is null) return null;

        var team = await db.Teams
            .AsNoTracking()
            .Include(t => t.Club)
            .FirstAsync(t => t.Id == game.TeamId, cancellationToken);

        return new MatchCalendar(team.FullName, [game]);
    }
}
