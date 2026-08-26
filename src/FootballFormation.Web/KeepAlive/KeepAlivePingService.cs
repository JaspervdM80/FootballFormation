namespace FootballFormation.Web.KeepAlive;

/// Fly suspends this machine after roughly five minutes idle and exposes no way to lengthen that. The ping must reach the public
/// hostname, because only traffic through Fly's edge counts as load. See docs/deployment.md.
public sealed class KeepAlivePingService(
    IHttpClientFactory httpClientFactory,
    KeepAliveTracker tracker,
    TimeProvider time,
    ILogger<KeepAlivePingService> logger) : BackgroundService
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    // Comfortably under Fly's ~5-minute idle sweep, so the proxy never sees a gap long enough to suspend the machine.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private const string PingUrl = "https://gjs-meiden.nl/health";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!tracker.RecentlyActive(Window))
                continue;

            try
            {
                using var client = httpClientFactory.CreateClient("KeepAlive");
                using var response = await client.GetAsync(PingUrl, stoppingToken);

                if (!response.IsSuccessStatusCode)
                    logger.LogWarning("Keep-alive ping to {Url} returned {StatusCode}", PingUrl, response.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Keep-alive ping to {Url} failed", PingUrl);
            }
        }
    }
}
