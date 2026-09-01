using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// Admin unless a test says otherwise, so the tests about something else read as though authorization were not there. Takes the team in
/// scope for the same reason CircuitCurrentUser does: "am I an admin" is a question about one team, and a fake that forgot which would
/// pass the tests that exist to catch that.
public sealed class FakeCurrentUser(FakeCurrentTeam currentTeam) : ICurrentUser
{
    public bool IsAdmin { get; set; } = true;

    /// Follows <see cref="IsAdmin"/> unless a test separates them, which is the whole point of the pair.
    public bool IsApplicationAdmin { get; set; } = true;

    /// Which team the admin runs. Null means every team, which is what a test about something other than team scoping wants.
    public int? AdminTeamId { get; set; }

    public Task<bool> IsAdminAsync() => IsAdminOfAsync(currentTeam.Id);

    public Task<bool> IsAdminOfAsync(int? teamId) =>
        Task.FromResult(IsAdmin && (AdminTeamId is null || AdminTeamId == teamId));

    public Task<bool> IsApplicationAdminAsync() => Task.FromResult(IsApplicationAdmin);
}

/// The team a test's calls are about. Null before anything is seeded, exactly as <see cref="CurrentTeam"/> answers then.
public sealed class FakeCurrentTeam : ICurrentTeam
{
    public int? Id { get; set; }

    public Task<int?> GetIdAsync() => Task.FromResult(Id);
}
