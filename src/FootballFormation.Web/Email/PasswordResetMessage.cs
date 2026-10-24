using System.Net;
using FootballFormation.UI;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.Email;

public static class PasswordResetMessage
{
    /// In the culture of the request that asked for it, which is the language the person was reading the form in.
    public static EmailMessage Build(PasswordResetMail mail, string link, IStringLocalizer<Strings> L)
    {
        var greeting = L["Hello {0},", mail.DisplayName].Value;
        var reason = L["Someone asked for a new password for the account {0}.", mail.Username].Value;
        var action = L["Choose a new password"].Value;
        var expiry = L["The link works once and expires in {0} minutes.", (int)PasswordLinkService.MailedLinkLifetime.TotalMinutes].Value;
        var ignore = L["Didn't ask for this? Then ignore this email; your password stays as it is."].Value;

        var text = $"{greeting}\n\n{reason}\n\n{action}: {link}\n\n{expiry}\n\n{ignore}\n";

        var html = $"""
            <p>{WebUtility.HtmlEncode(greeting)}</p>
            <p>{WebUtility.HtmlEncode(reason)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(action)}</a></p>
            <p>{WebUtility.HtmlEncode(expiry)}</p>
            <p>{WebUtility.HtmlEncode(ignore)}</p>
            """;

        return new EmailMessage(mail.Email, action, text, html);
    }
}
