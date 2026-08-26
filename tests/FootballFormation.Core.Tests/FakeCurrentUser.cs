using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// Admin unless a test says otherwise, so the tests about something else read as though authorization were not there.
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAdmin { get; set; } = true;

    public Task<bool> IsAdminAsync() => Task.FromResult(IsAdmin);
}
