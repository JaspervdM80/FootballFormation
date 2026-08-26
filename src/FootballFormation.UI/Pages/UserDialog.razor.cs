namespace FootballFormation.UI.Pages;

/// <summary>
/// Adds an account, edits one, or sets a password — three shapes of the same form, because they
/// share every field and all three hand the page back the same value to save.
/// </summary>
public partial class UserDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The account being edited, or null when adding one.</summary>
    [Parameter] public AppUser? User { get; set; }

    /// <summary>Reset-password mode: only the password fields, for an account that already exists.</summary>
    [Parameter] public bool PasswordOnly { get; set; }

    private MudForm Form { get; set; } = null!;

    private string DisplayName { get; set; } = string.Empty;
    private string Username { get; set; } = string.Empty;
    private UserRole Role { get; set; } = UserRole.Admin;
    private string Password { get; set; } = string.Empty;
    private string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>A new account needs a password; an existing one gets it from Reset Password instead.</summary>
    private bool ShowPasswordFields => PasswordOnly || User is null;

    protected override void OnParametersSet()
    {
        if (User is null) return;

        DisplayName = User.DisplayName;
        Username = User.Username;
        Role = User.Role;
    }

    private string? ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return L["Password is required"].Value;

        return password.Length < UserService.MinPasswordLength
            ? L["Password must be at least {0} characters", UserService.MinPasswordLength].Value
            : null;
    }

    private string? ValidateConfirmation(string confirmation) =>
        confirmation == Password ? null : L["Passwords do not match"].Value;

    /// <summary>
    /// Duplicate logins and the last-admin rule live in <c>UserService</c>, so they apply to every
    /// caller; the dialog only checks what the form itself can.
    /// </summary>
    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        MudDialog.Close(DialogResult.Ok(new Model
        {
            DisplayName = DisplayName,
            Username = Username,
            Role = Role,
            Password = Password
        }));
    }

    private void Cancel() => MudDialog.Cancel();

    /// <summary>What the dialog hands back. Not an <see cref="AppUser"/> — that carries a password
    /// hash and a security stamp the dialog has no business constructing.</summary>
    public sealed class Model
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public UserRole Role { get; init; }
        public string Password { get; init; } = string.Empty;
    }
}
