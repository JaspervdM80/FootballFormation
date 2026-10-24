using System.Threading.Channels;

namespace FootballFormation.Web.Email;

/// Mail goes out after the response, not during it: a request that waited on the provider only when an account matched would let its
/// timing say which addresses have one.
public sealed class EmailOutbox(IEmailSender sender, ILogger<EmailOutbox> logger) : BackgroundService
{
    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(EmailMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
            logger.LogWarning("Dropped a mail: the outbox is full");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to send a mail");
            }
        }
    }
}
