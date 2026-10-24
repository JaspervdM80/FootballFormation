namespace FootballFormation.Core.Models;

/// A single-use link that lets someone choose the password for an account — an invite an admin copies, or a reset mailed on request.
/// Only the token's hash is stored, so the table cannot be read back into working links.
public class PasswordLink
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    /// The account's stamp when the link was issued: any password or authority change since rolls it, and that retires the link too.
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public bool IsUsableAt(DateTime utcNow, string currentStamp) => utcNow < ExpiresUtc && SecurityStamp == currentStamp;
}
