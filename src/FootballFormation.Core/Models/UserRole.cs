namespace FootballFormation.Core.Models;

/// Written into the cookie by name, so a member's name is exactly what [Authorize(Roles = ...)] matches against and the two cannot drift.
/// Never renumber a member — the numbers are in the database.
public enum UserRole
{
    Admin = 1
}
