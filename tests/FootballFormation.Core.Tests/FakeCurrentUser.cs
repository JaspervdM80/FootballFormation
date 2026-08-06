using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The caller a test is pretending to be. Admin unless a test says otherwise, so the tests that
/// are about something else read as though authorization were not there.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAdmin { get; set; } = true;

    public Task<bool> IsAdminAsync() => Task.FromResult(IsAdmin);
}
