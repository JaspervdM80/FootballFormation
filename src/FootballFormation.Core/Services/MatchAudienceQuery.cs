using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Services;

public record MatchAudience(
    MatchNotification Notification,
    string TeamName,
    int GameId,
    bool IsFinished,
    IReadOnlyList<PushSubscription> Followers);

/// Everything the push sender needs about one match, read in a single pass. Here rather than in the host because the include chains are
/// Core's (see GameQueries), and on the raw factory because this runs outside any request and so has no team in scope — the game names
/// its own team, which is then stamped on so the reads below scope themselves as they would anywhere else.
public sealed class MatchAudienceQuery(IRawDbContextFactory dbFactory)
{
    public async Task<MatchAudience?> ForAsync(int gameId, LiveMatchEvent change, CancellationToken cancellationToken = default)
    {
        await using var db = dbFactory.CreateDbContext();

        var team = await db.Games
            .IgnoreQueryFilters()
            .Where(g => g.Id == gameId)
            .Select(g => db.Teams.Where(t => t.Id == g.TeamId).Select(t => new { t.Id, t.ClubId, t.Name, ClubName = t.Club!.Name }).First())
            .FirstOrDefaultAsync(cancellationToken);

        if (team is null) return null;

        db.CurrentTeamId = team.Id;
        db.CurrentClubId = team.ClubId;

        var game = await db.Games
            .AsNoTrackingWithIdentityResolution()
            .WithPeriods()
            .WithGoalsAndScorers()
            .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);

        if (game is null) return null;

        var followers = await db.PushSubscriptions.AsNoTracking().ToListAsync(cancellationToken);
        if (followers.Count == 0) return null;

        return new MatchAudience(
            MatchNotificationReport.Build(game, change),
            $"{team.ClubName} {team.Name}",
            gameId,
            game.MatchState == MatchState.Finished,
            followers);
    }

    /// A push service answering 404 or 410 is saying this browser is gone for good, and nothing else ever removes the row.
    public async Task DropAsync(IReadOnlyList<string> endpoints)
    {
        if (endpoints.Count == 0) return;

        await using var db = dbFactory.CreateDbContext();

        await db.PushSubscriptions
            .IgnoreQueryFilters()
            .Where(s => endpoints.Contains(s.Endpoint))
            .ExecuteDeleteAsync();
    }
}
