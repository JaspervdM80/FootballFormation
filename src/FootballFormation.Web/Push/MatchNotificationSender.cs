using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using FootballFormation.Core.Models;
using FootballFormation.Core.Push;
using FootballFormation.UI;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.Extensions.Localization;

namespace FootballFormation.Web.Push;

/// Turns a touchline write into a notification on the phones that asked for one. Only kick-off, a goal and full time reach here; every
/// other live change redraws an open screen and wakes nobody.
public sealed class MatchNotificationSender(
    LiveMatchNotifier notifier,
    MatchAudienceQuery audience,
    IHttpClientFactory httpClientFactory,
    PushConfiguration push,
    IStringLocalizer<Strings> localizer,
    TimeProvider time,
    ILogger<MatchNotificationSender> logger) : BackgroundService
{
    /// Bounded and dropping: a backlog nobody could keep up with is worth less than the touchline write staying fast, and the queue only
    /// ever holds one match's events.
    private readonly Channel<Queued> _queue = Channel.CreateBounded<Queued>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });

    private const int MaxAttempts = 3;

    /// Enough to clear a squad's worth of parents quickly without opening a connection per follower against one push service.
    private const int MaxParallelSends = 8;

    private readonly record struct Queued(int GameId, LiveMatchEvent Change);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!push.IsConfigured)
        {
            logger.LogInformation("Match notifications are off: no VAPID keys are configured");
            return;
        }

        notifier.Changed += Enqueue;

        try
        {
            await foreach (var queued in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(queued, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not notify anyone about game {GameId}", queued.GameId);
                }
            }
        }
        finally
        {
            notifier.Changed -= Enqueue;
        }
    }

    /// Raised inside the touchline write, so this only ever hands the work over: a subscriber that blocks would make an admin's tap wait
    /// on a push service, and one that throws would fail the write itself. See LiveMatchOperation.
    private void Enqueue(int gameId, LiveMatchEvent change)
    {
        if (change is LiveMatchEvent.Other) return;

        _queue.Writer.TryWrite(new Queued(gameId, change));
    }

    private async Task SendAsync(Queued queued, CancellationToken cancellationToken)
    {
        if (await audience.ForAsync(queued.GameId, queued.Change, cancellationToken) is not { } match) return;

        var url = match.IsFinished ? AppRoutes.Result(match.GameId) : AppRoutes.Live(match.GameId);

        var payloads = MatchPayloads.For(
            match.Followers.Select(f => f.Culture),
            url,
            match.GameId,
            () => MatchNotificationTextBuilder.Build(match.Notification, localizer));

        var gone = new ConcurrentBag<string>();

        // In parallel because a retry now costs seconds: sent one at a time, the last of two hundred phones would hear about a goal
        // long after the next one was scored.
        await Parallel.ForEachAsync(
            match.Followers,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSends, CancellationToken = cancellationToken },
            async (follower, token) =>
            {
                if (!await DeliverAsync(follower, payloads[follower.Culture], token)) gone.Add(follower.Endpoint);
            });

        await audience.DropAsync([.. gone]);

        logger.LogInformation("Notified {Count} follower(s) of {Change} in game {GameId}, dropping {Gone} that had gone away",
            match.Followers.Count - gone.Count, queued.Change, queued.GameId, gone.Count);
    }

    private Task<bool> DeliverAsync(PushSubscription follower, byte[] payload, CancellationToken cancellationToken) =>
        PushDeliveryOutcome.DeliverAsync(
            () => TryDeliverAsync(follower, payload, cancellationToken),
            attempt => Task.Delay(TimeSpan.FromSeconds(attempt), time, cancellationToken),
            MaxAttempts);

    private async Task<PushDelivery> TryDeliverAsync(
        PushSubscription follower, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            // Inside the try, not in the initialiser: a key that survived validation but will not import throws here, and outside it the
            // throw would escape Parallel.ForEachAsync and take the whole match's fan-out down with this one follower.
            var sealedPayload = WebPush.Encrypt(payload, follower.P256dh, follower.Auth);

            // A fresh message each attempt: an HttpRequestMessage cannot be sent twice.
            using var request = new HttpRequestMessage(HttpMethod.Post, follower.Endpoint)
            {
                Content = new ByteArrayContent(sealedPayload)
            };

            request.Content.Headers.ContentType = new("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("TTL", "3600");
            request.Headers.TryAddWithoutValidation("Urgency", "high");
            request.Headers.TryAddWithoutValidation(
                "Authorization", WebPush.Authorization(follower.Endpoint, push.Keys!, time.GetUtcNow()));

            var client = httpClientFactory.CreateClient("WebPush");
            using var response = await client.SendAsync(request, cancellationToken);

            var outcome = PushDeliveryOutcome.For(response.StatusCode);

            if (outcome is PushDelivery.Refused)
                logger.LogWarning("A push service refused a notification with {StatusCode}", response.StatusCode);

            return outcome;
        }
        // Never worth retrying: the same key will fail to import every time. The row stays, because only the push service gets to say a
        // browser is gone.
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "A stored subscription key could not be used");
            return PushDelivery.Refused;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not reach a push service");
            return PushDelivery.Retry;
        }
    }
}
