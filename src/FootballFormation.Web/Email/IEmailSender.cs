namespace FootballFormation.Web.Email;

/// Resend in production and a pickup directory in development, so the outbox depends on neither.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string Text, string Html);
