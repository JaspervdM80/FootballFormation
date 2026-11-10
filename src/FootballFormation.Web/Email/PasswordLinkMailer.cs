using FootballFormation.UI;
using FootballFormation.UI.Mail;
using FootballFormation.UI.Navigation;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.Email;

public sealed class PasswordLinkMailer(EmailConfiguration configuration, EmailOutbox outbox, IStringLocalizer<Strings> localizer)
    : IPasswordLinkMailer
{
    public bool CanSend => configuration.IsConfigured;

    public string LinkFor(string token, string pageBaseUri) =>
        AppRoutes.PasswordLinkUrl(configuration.PublicUrl?.ToString() ?? pageBaseUri, token);

    public bool Send(IssuedPasswordLink link)
    {
        if (!CanSend || link.Email is null) return false;

        outbox.Enqueue(PasswordInviteMessage.Build(link, AppRoutes.PasswordLinkUrl(configuration.PublicUrl!.ToString(), link.Token), localizer));
        return true;
    }
}
