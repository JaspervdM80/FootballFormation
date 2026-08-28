using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

/// Admin unless a test says otherwise, so the tests about something else read as though authorization were not there.
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAdmin { get; set; } = true;

    /// Follows <see cref="IsAdmin"/> unless a test separates them, which is the whole point of the pair.
    public bool IsApplicationAdmin { get; set; } = true;

    public Task<bool> IsAdminAsync() => Task.FromResult(IsAdmin);

    public Task<bool> IsApplicationAdminAsync() => Task.FromResult(IsApplicationAdmin);
}
