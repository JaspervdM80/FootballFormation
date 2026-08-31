namespace FootballFormation.UI.Pages;

/// Three shapes of one form, because they share every field and all hand the page back the same value to save.
public partial class UserDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Null when adding an account rather than editing one.
    [Parameter] public UserSummary? User { get; set; }

    /// Reset-password mode: only the password fields, for an account that already exists.
    [Parameter] public bool PasswordOnly { get; set; }

    /// Whether the signed-in admin holds the application-admin role themselves — the only ones who may hand it out or take it away.
    [Parameter] public bool CanGrantApplicationAdmin { get; set; }

    private MudForm Form { get; set; } = null!;

    private string DisplayName { get; set; } = string.Empty;
    private string Username { get; set; } = string.Empty;
    private UserRole Role { get; set; } = UserRole.Admin;
    private string Password { get; set; } = string.Empty;
    private string ConfirmPassword { get; set; } = string.Empty;

    /// A new account needs a password; an existing one gets it from Reset Password instead.
    private bool ShowPasswordFields => PasswordOnly || User is null;

    /// Editing an application admin without being one: UserService refuses either direction, so the field says so instead of offering it.
    private bool RoleLocked => !CanGrantApplicationAdmin && User?.Role == UserRole.ApplicationAdmin;

    private IEnumerable<UserRole> SelectableRoles => CanGrantApplicationAdmin || RoleLocked
        ? Enum.GetValues<UserRole>()
        : Enum.GetValues<UserRole>().Where(r => r != UserRole.ApplicationAdmin);

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

    /// Duplicate logins and the last-admin rule live in UserService so they apply to every caller; this checks only what the form can.
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

    /// Not an <see cref="AppUser"/> — that carries a password hash and a security stamp the dialog has no business constructing.
    public sealed class Model
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public UserRole Role { get; init; }
        public string Password { get; init; } = string.Empty;
    }
}
