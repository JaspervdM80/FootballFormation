namespace FootballFormation.Core.Services;

/// Every step counts across all seasons, so the checklist is a new team's and does not come back when a season rolls over.
public record TeamSetup(bool HasMatchSettings, bool HasSquad, bool HasTrainings, bool HasGames, bool IsHidden)
{
    private bool[] Steps => [HasMatchSettings, HasSquad, HasTrainings, HasGames];

    public int StepCount => Steps.Length;

    public int DoneCount => Steps.Count(done => done);

    public bool IsComplete => DoneCount == StepCount;

    public bool Shows => !IsHidden && !IsComplete;
}

public class TeamSetupService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    ILogger<TeamSetupService> logger)
{
    public Task<Result<TeamSetup>> GetAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "load the team setup", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var team = await db.Teams
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == db.CurrentTeamId, cancellationToken);
            if (team is null)
                return Result.Failure<TeamSetup>("Team with ID {0} not found", db.CurrentTeamId ?? 0);

            var setup = new TeamSetup(
                HasMatchSettings: await db.MatchPreferences.AnyAsync(p => p.DefaultsConfirmed, cancellationToken),
                HasSquad: await db.SeasonSquadMembers.AnyAsync(cancellationToken),
                HasTrainings: await db.Trainings.AnyAsync(cancellationToken),
                HasGames: await db.Games.AnyAsync(cancellationToken),
                IsHidden: team.SetupChecklistHidden);

            logger.LogDebug("Team {TeamId} setup: {DoneCount} of {StepCount} done, hidden {IsHidden}",
                team.Id, setup.DoneCount, setup.StepCount, setup.IsHidden);
            return Result.Success(setup);
        });

    public Task<Result> HideAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAdminAsync(currentUser, logger, "hide the checklist", cancellationToken, async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == db.CurrentTeamId, cancellationToken);
            if (team is null)
            {
                logger.LogWarning("Cannot hide the setup checklist: team {TeamId} not found", db.CurrentTeamId ?? 0);
                return Result.Failure("Team with ID {0} not found", db.CurrentTeamId ?? 0);
            }

            team.SetupChecklistHidden = true;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Hid the setup checklist for team {TeamId}", team.Id);
            return Result.Success();
        });
}
