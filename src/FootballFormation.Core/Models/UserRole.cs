namespace FootballFormation.Core.Models;

/// <summary>
/// What an account is allowed to do. Stored as an int on <see cref="AppUser.Role"/>, and written
/// into the auth cookie by name at login — <c>Admin.ToString()</c> is exactly the string
/// <c>[Authorize(Roles = ...)]</c> and <c>&lt;AuthorizeView Roles="..."&gt;</c> match against, so
/// the enum member name and the claim value cannot drift apart.
/// <para>
/// Only <see cref="Admin"/> exists today; not being signed in at all is the "anonymous" case and
/// needs no member. Adding a role is a new member here plus deciding what it reaches — never
/// renumber an existing one, the numbers are in the database.
/// </para>
/// </summary>
public enum UserRole
{
    Admin = 1
}
