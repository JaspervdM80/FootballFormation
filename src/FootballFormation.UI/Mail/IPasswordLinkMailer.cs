namespace FootballFormation.UI.Mail;

/// The host's mail, which this library cannot reach itself: Web references UI, not the other way round.
public interface IPasswordLinkMailer
{
    /// False where no mail is configured, which leaves /users with the copied link alone.
    bool CanSend { get; }

    /// On the configured public address whichever domain the admin is on, so a copied link and a mailed one open the same place.
    string LinkFor(string token, string pageBaseUri);

    /// Queues the link to the account's own address; false when it has none or nothing can be sent.
    bool Send(IssuedPasswordLink link);
}
