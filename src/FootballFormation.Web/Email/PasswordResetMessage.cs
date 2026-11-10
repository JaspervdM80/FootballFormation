using FootballFormation.UI;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.Email;

public static class PasswordResetMessage
{
    /// In the culture of the request that asked for it, which is the language the person was reading the form in.
    public static EmailMessage Build(PasswordResetMail mail, string link, IStringLocalizer<Strings> L) =>
        LinkMail.Compose(mail.Email, L["Choose a new password"].Value, link,
            [L["Hello {0},", mail.DisplayName].Value, L["Someone asked for a new password for the account {0}.", mail.Username].Value],
            [
                L["The link works once and expires in {0} minutes.", (int)PasswordLinkService.MailedLinkLifetime.TotalMinutes].Value,
                L["Didn't ask for this? Then ignore this email; your password stays as it is."].Value
            ]);
}
