using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// The whole of "which team may this account change", in one function, tested structurally — the same reason AuthorizationTests exists.
/// A missed team check does not announce itself.
public class TeamAuthorityTests
{
    [Fact]
    public void An_admin_may_change_the_team_they_run()
    {
        Assert.True(TeamAuthority.GrantsAdminOf(isApplicationAdmin: false, accountTeamId: 1, teamId: 1));
    }

    [Fact]
    public void An_admin_may_not_change_another_team()
    {
        Assert.False(TeamAuthority.GrantsAdminOf(isApplicationAdmin: false, accountTeamId: 1, teamId: 2));
    }

    [Fact]
    public void An_application_admin_may_change_every_team()
    {
        Assert.True(TeamAuthority.GrantsAdminOf(isApplicationAdmin: true, accountTeamId: null, teamId: 1));
        Assert.True(TeamAuthority.GrantsAdminOf(isApplicationAdmin: true, accountTeamId: null, teamId: 2));
    }

    [Fact]
    public void An_admin_on_no_team_may_change_nothing()
    {
        // The state a database written before teams would come back in, had the migration not backfilled it: the answer has to be no,
        // not "matches, both are null".
        Assert.False(TeamAuthority.GrantsAdminOf(isApplicationAdmin: false, accountTeamId: null, teamId: null));
        Assert.False(TeamAuthority.GrantsAdminOf(isApplicationAdmin: false, accountTeamId: null, teamId: 1));
    }

    [Fact]
    public void No_team_in_scope_is_refused_even_to_the_account_that_runs_one()
    {
        // An unseeded database has no team, and "nothing is in scope" must not read as "everything is".
        Assert.False(TeamAuthority.GrantsAdminOf(isApplicationAdmin: false, accountTeamId: 1, teamId: null));
    }
}
