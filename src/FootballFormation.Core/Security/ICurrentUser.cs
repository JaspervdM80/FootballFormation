namespace FootballFormation.Core.Security;

/// The second line behind the render tree: an AuthorizeView is the only thing stopping a service reached another way — a minimal API, a
/// control that outgrows its wrapper — from running for anybody. An interface because it genuinely has two implementations.
public interface ICurrentUser
{
    /// Async because a circuit resolves its principal through AuthenticationStateProvider, which is asynchronous.
    Task<bool> IsAdminAsync();
}
