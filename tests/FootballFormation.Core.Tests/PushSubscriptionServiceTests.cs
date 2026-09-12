using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// The only write in the app an anonymous caller can reach, so the checks that stand in for the admin guard are what is tested hardest
/// here.
public class PushSubscriptionServiceTests : ServiceTestBase
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc123";
    private const string Key = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";

    private readonly PushSubscriptionService _push;

    public PushSubscriptionServiceTests()
    {
        SeedTeam();
        _push = new PushSubscriptionService(DbFactory, CurrentTeam, Time, NullLogger<PushSubscriptionService>.Instance);
    }

    [Fact]
    public async Task A_browser_that_asks_to_follow_is_stored_against_the_team_in_scope()
    {
        Assert.True((await _push.SubscribeAsync(Endpoint, Key, Auth, "nl")).IsSuccess);

        var stored = await Read().PushSubscriptions.SingleAsync();

        Assert.Equal(Endpoint, stored.Endpoint);
        Assert.Equal(CurrentTeam.Id, stored.TeamId);
        Assert.Equal("nl", stored.Culture);
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task Nobody_has_to_be_signed_in_to_follow_a_match()
    {
        CurrentUser.IsAdmin = false;

        Assert.True((await _push.SubscribeAsync(Endpoint, Key, Auth, "nl")).IsSuccess);
        Assert.Equal(1, await Read().PushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Subscribing_twice_moves_the_one_row_rather_than_adding_a_second()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");
        await _push.SubscribeAsync(Endpoint, Key, Auth, "en");

        var stored = await Read().PushSubscriptions.SingleAsync();
        Assert.Equal("en", stored.Culture);
    }

    /// The endpoint is unique across every team, so the same browser following a second team has to be found past the team filter and
    /// moved — inserting again would hit the unique index instead.
    [Fact]
    public async Task A_browser_that_switches_team_follows_the_new_one_only()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");
        var first = CurrentTeam.Id;

        var second = SeedTeam("GJS", "MO17-1");
        Assert.True((await _push.SubscribeAsync(Endpoint, Key, Auth, "nl")).IsSuccess);

        var stored = await Read().PushSubscriptions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(second.Id, stored.TeamId);
        Assert.NotEqual(first, stored.TeamId);
    }

    [Fact]
    public async Task One_team_never_sees_anothers_followers()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        SeedTeam("GJS", "MO17-1");

        Assert.Empty(await Read().PushSubscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/send/x")]
    [InlineData("https://127.0.0.1/send/x")]
    [InlineData("https://localhost/send/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public async Task An_endpoint_that_is_not_a_push_services_is_refused(string endpoint)
    {
        Assert.True((await _push.SubscribeAsync(endpoint, Key, Auth, "nl")).IsFailure);
        Assert.Empty(await Read().PushSubscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("", Auth)]
    [InlineData("not-base64url!", Auth)]
    [InlineData("BTBZMqHH6r4Tts7J_aSIgg", Auth)]
    [InlineData(Key, "")]
    [InlineData(Key, "c2hvcnQ")]
    public async Task A_key_of_the_wrong_shape_is_refused(string key, string auth)
    {
        Assert.True((await _push.SubscribeAsync(Endpoint, key, auth, "nl")).IsFailure);
        Assert.Empty(await Read().PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task A_culture_the_app_does_not_serve_is_refused()
    {
        Assert.True((await _push.SubscribeAsync(Endpoint, Key, Auth, "de")).IsFailure);
        Assert.Empty(await Read().PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Unsubscribing_removes_the_row_whichever_team_is_in_scope()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");
        SeedTeam("GJS", "MO17-1");

        Assert.True((await _push.UnsubscribeAsync(Endpoint)).IsSuccess);
        Assert.Empty(await Read().PushSubscriptions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Unsubscribing_something_never_stored_is_not_a_failure()
    {
        Assert.True((await _push.UnsubscribeAsync(Endpoint)).IsSuccess);
    }
}
