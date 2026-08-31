namespace FootballFormation.Core.Models;

/// The account list without the credential columns. <see cref="Services.UserService.GetAllAsync"/> hands this back rather than the entity,
/// so the password hash and security stamp never leave the service for a page — or a log — that has no use for them.
public record UserSummary(int Id, string DisplayName, string Username, UserRole Role);
