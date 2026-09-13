using System.Net;
using FootballFormation.Core.Push;

namespace FootballFormation.Core.Tests;

/// The table that decides whether a follower keeps getting notifications. Getting Gone wrong either strands a browser nobody will ever
/// reach again or unsubscribes one that was only having a bad minute — and a follower who never opens the app cannot repair either.
public class PushDeliveryOutcomeTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void A_push_service_that_took_it_is_done_with(HttpStatusCode status) =>
        Assert.Equal(PushDelivery.Sent, PushDeliveryOutcome.For(status));

    /// The only two answers that remove a subscription.
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void Only_a_browser_that_is_gone_for_good_is_dropped(HttpStatusCode status) =>
        Assert.Equal(PushDelivery.Gone, PushDeliveryOutcome.For(status));

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void A_bad_minute_is_worth_another_attempt(HttpStatusCode status) =>
        Assert.Equal(PushDelivery.Retry, PushDeliveryOutcome.For(status));

    /// Our own mistake — a malformed payload, an expired or wrong VAPID signature. Retrying would fail identically and only delay every
    /// other follower behind it.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public void Our_own_bad_request_is_not_retried(HttpStatusCode status) =>
        Assert.Equal(PushDelivery.Refused, PushDeliveryOutcome.For(status));

    /// Reachable only because the WebPush client has AllowAutoRedirect off — otherwise HttpClient follows the redirect and this is never
    /// asked. Following one would send an encrypted payload somewhere the subscription never named, past IsPushEndpoint.
    [Fact]
    public void A_redirect_is_not_taken_for_success() =>
        Assert.Equal(PushDelivery.Refused, PushDeliveryOutcome.For(HttpStatusCode.Found));

    private static Task NoWait(int attempt) => Task.CompletedTask;

    [Fact]
    public async Task A_delivery_that_lands_is_not_retried()
    {
        var attempts = 0;

        Assert.True(await PushDeliveryOutcome.DeliverAsync(
            () => { attempts++; return Task.FromResult(PushDelivery.Sent); }, NoWait, 3));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_bad_minute_is_tried_again_until_it_lands()
    {
        var attempts = 0;

        Assert.True(await PushDeliveryOutcome.DeliverAsync(
            () =>
            {
                attempts++;
                return Task.FromResult(attempts < 3 ? PushDelivery.Retry : PushDelivery.Sent);
            }, NoWait, 3));

        Assert.Equal(3, attempts);
    }

    /// The inversion the reviewer named: read the other way round, a push service having a bad afternoon unsubscribes a whole ground and
    /// nobody finds out until the next match is silent.
    [Fact]
    public async Task A_run_of_bad_minutes_never_unsubscribes_anyone()
    {
        var attempts = 0;

        Assert.True(await PushDeliveryOutcome.DeliverAsync(
            () => { attempts++; return Task.FromResult(PushDelivery.Retry); }, NoWait, 3));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Only_a_browser_that_is_gone_asks_to_be_pruned()
    {
        Assert.False(await PushDeliveryOutcome.DeliverAsync(
            () => Task.FromResult(PushDelivery.Gone), NoWait, 3));

        Assert.True(await PushDeliveryOutcome.DeliverAsync(
            () => Task.FromResult(PushDelivery.Refused), NoWait, 3));
    }

    [Fact]
    public async Task The_wait_grows_with_each_attempt_and_never_follows_the_last_one()
    {
        var waits = new List<int>();

        await PushDeliveryOutcome.DeliverAsync(
            () => Task.FromResult(PushDelivery.Retry),
            attempt => { waits.Add(attempt); return Task.CompletedTask; },
            3);

        Assert.Equal([1, 2], waits);
    }
}
