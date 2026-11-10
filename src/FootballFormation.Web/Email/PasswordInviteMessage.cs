using FootballFormation.UI;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.Email;

public static class PasswordInviteMessage
{
    /// In the admin's language, which is the culture of the circuit that sent it.
    public static EmailMessage Build(IssuedPasswordLink issued, string link, IStringLocalizer<Strings> L) =>
        LinkMail.Compose(issued.Email ?? throw new ArgumentException("The account has no address to mail.", nameof(issued)),
            L["Choose your password"].Value, link,
            [L["Hello {0},", issued.DisplayName].Value, L["Choose a password for your account {0} with the link below.", issued.Username].Value],
            [L["The link works once and expires in {0} days.", (int)PasswordLinkService.CopiedLinkLifetime.TotalDays].Value]);
}
