namespace FootballFormation.Core.Security;

/// The second line behind the render tree: an AuthorizeView is the only thing stopping a service reached another way — a minimal API, a
/// control that outgrows its wrapper — from running for anybody. An interface because it genuinely has two implementations.
public interface ICurrentUser
{
    /// Admin of the team the request is about (<see cref="ICurrentTeam"/>), which is what every write guard asks. Async because a
    /// circuit resolves its principal through AuthenticationStateProvider, which is asynchronous.
    Task<bool> IsAdminAsync();

    /// Admin of one named team, for a write whose subject is not the team in scope — an account belonging to another one.
    Task<bool> IsAdminOfAsync(int? teamId);

    /// True implies both of the above are true as well.
    Task<bool> IsApplicationAdminAsync();
}
