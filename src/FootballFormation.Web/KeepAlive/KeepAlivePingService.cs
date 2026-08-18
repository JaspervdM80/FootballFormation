namespace FootballFormation.Web.KeepAlive;

/// <summary>
/// Fly's proxy suspends this machine after roughly five minutes with no inbound traffic — a window
/// it does not expose a way to lengthen (see docs/deployment.md). This keeps the machine awake for
/// up to 30 minutes after the last real visitor instead, so a coach glancing away mid-match for a
/// few minutes doesn't pay a resume on every return.
/// <para>
/// The ping has to reach the public hostname, not <c>localhost</c> or the <c>.internal</c> address:
/// only traffic that passes through Fly's edge counts as load for its autostop decision. It reuses
/// the existing <c>/health</c> endpoint rather than a dedicated one — already anonymous, already
/// cheap. The ping's own request must not count as activity, or this loop would renew its own
/// window forever — <c>Program.cs</c> excludes <c>/health</c> from <see cref="KeepAliveTracker.Touch"/>
/// for that reason.
/// </para>
/// </summary>
public sealed class KeepAlivePingService(
    IHttpClientFactory httpClientFactory,
    KeepAliveTracker tracker,
    TimeProvider time,
    ILogger<KeepAlivePingService> logger) : BackgroundService
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    // Comfortably under Fly's ~5-minute idle sweep, so the proxy never sees a continuous gap long
    // enough to suspend the machine while a ping is still due.
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
