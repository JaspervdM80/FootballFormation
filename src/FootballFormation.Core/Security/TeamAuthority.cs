namespace FootballFormation.Core.Security;

/// Whether an account may change one team's data. A function rather than a condition inside each <see cref="ICurrentUser"/>
/// implementation, so the rule is testable on its own — AuthorizationTests is the precedent.
public static class TeamAuthority
{
    /// <paramref name="teamId"/> is the team being changed and <paramref name="accountTeamId"/> the one the account runs. A null
    /// team on either side grants nothing: an application admin is the only account above a single team.
    public static bool GrantsAdminOf(bool isApplicationAdmin, int? accountTeamId, int? teamId) =>
        isApplicationAdmin || (teamId is not null && accountTeamId == teamId);
}
