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

    /// A redirect is not a delivery, and following one would send an encrypted payload somewhere the subscription never named.
    [Fact]
    public void A_redirect_is_not_taken_for_success() =>
        Assert.Equal(PushDelivery.Refused, PushDeliveryOutcome.For(HttpStatusCode.Found));
}
