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

    private const string Rotated = "https://fcm.googleapis.com/fcm/send/rotated";

    /// The service worker renews with no page and no person, so the team and language have to survive the rotation untouched — a browser
    /// that rotated an endpoint has chosen nothing new.
    [Fact]
    public async Task A_rotated_endpoint_keeps_the_team_and_language_it_was_following_with()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "en");
        var team = CurrentTeam.Id;

        // As the worker does it: whatever team the worker's cookies happen to name is not the follower's choice.
        SeedTeam("GJS", "MO17-1");

        Assert.True((await _push.RenewAsync(Endpoint, Rotated, Key, Auth)).IsSuccess);

        var stored = await Read().PushSubscriptions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(Rotated, stored.Endpoint);
        Assert.Equal(team, stored.TeamId);
        Assert.Equal("en", stored.Culture);
    }

    [Fact]
    public async Task Renewing_something_never_stored_changes_nothing()
    {
        Assert.True((await _push.RenewAsync(Endpoint, Rotated, Key, Auth)).IsFailure);
        Assert.Empty(await Read().PushSubscriptions.IgnoreQueryFilters().ToListAsync());
    }

    /// Rotating onto an endpoint already on file would otherwise hit the unique index and lose the renewal entirely.
    [Fact]
    public async Task A_rotation_onto_an_endpoint_already_held_replaces_it()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");
        await _push.SubscribeAsync(Rotated, Key, Auth, "nl");

        Assert.True((await _push.RenewAsync(Endpoint, Rotated, Key, Auth)).IsSuccess);

        var stored = await Read().PushSubscriptions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(Rotated, stored.Endpoint);
    }

    [Fact]
    public async Task A_renewal_to_an_unusable_endpoint_is_refused()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        Assert.True((await _push.RenewAsync(Endpoint, "https://127.0.0.1/send/x", Key, Auth)).IsFailure);
        Assert.Equal(Endpoint, (await Read().PushSubscriptions.SingleAsync()).Endpoint);
    }

    /// What stops the toggle reading "on" off the browser's own copy while the row behind it has been pruned.
    [Fact]
    public async Task The_server_is_what_says_whether_a_browser_still_follows_this_team()
    {
        Assert.False((await _push.FollowsCurrentTeamAsync(Endpoint)).Value);

        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");
        Assert.True((await _push.FollowsCurrentTeamAsync(Endpoint)).Value);

        await _push.UnsubscribeAsync(Endpoint);
        Assert.False((await _push.FollowsCurrentTeamAsync(Endpoint)).Value);
    }

    [Fact]
    public async Task Following_one_team_does_not_read_as_following_another()
    {
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        SeedTeam("GJS", "MO17-1");

        Assert.False((await _push.FollowsCurrentTeamAsync(Endpoint)).Value);
    }
}
