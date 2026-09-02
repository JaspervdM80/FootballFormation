namespace FootballFormation.Core.Models;

/// Written into the cookie by name, so a member's name is exactly what [Authorize(Roles = ...)] matches against and the two cannot drift.
/// Never renumber a member — the numbers are in the database.
public enum UserRole
{
    Admin = 1,

    /// Implies <see cref="Admin"/> — Routing.PrincipalFor mints both role claims, so an application admin passes every
    /// admin check as well as the ones only it passes.
    ApplicationAdmin = 2
}
