using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// The sender runs outside any request, so this is the one read in the app with no team in scope to start from. Everything here is about
/// it finding the right team anyway — and only that team's followers.
public class MatchAudienceQueryTests : LiveMatchTestBase
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc123";
    private const string Key = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";

    private readonly MatchAudienceQuery _audience;
    private readonly PushSubscriptionService _push;

    public MatchAudienceQueryTests()
    {
        _audience = new MatchAudienceQuery(RawDbFactory);
        _push = new PushSubscriptionService(DbFactory, CurrentTeam, Time, NullLogger<PushSubscriptionService>.Instance);
    }

    [Fact]
    public async Task A_match_finds_its_own_team_and_that_teams_followers()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(8));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        // As the sender does: nothing in scope, the game id alone.
        CurrentTeam.Id = null;
        CurrentTeam.ClubId = null;

        var match = await _audience.ForAsync(game.Id, LiveMatchEvent.Goal);

        Assert.NotNull(match);
        Assert.Equal("GJS MO15-2", match.TeamName);
        Assert.Equal("P2", match.Notification.ScorerName);
        Assert.Equal(Endpoint, Assert.Single(match.Followers).Endpoint);
    }

    [Fact]
    public async Task A_match_never_reaches_another_teams_followers()
    {
        var game = await SeedGameAsync();
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        // A second team with a follower of its own, which this match has nothing to do with.
        SeedTeam("GJS", "MO17-1");
        await _push.SubscribeAsync("https://fcm.googleapis.com/fcm/send/other", Key, Auth, "en");

        CurrentTeam.Id = null;
        CurrentTeam.ClubId = null;

        var match = await _audience.ForAsync(game.Id, LiveMatchEvent.KickOff);

        Assert.Equal(Endpoint, Assert.Single(match!.Followers).Endpoint);
    }

    [Fact]
    public async Task A_match_nobody_follows_is_nothing_to_send()
    {
        var game = await SeedGameAsync();

        Assert.Null(await _audience.ForAsync(game.Id, LiveMatchEvent.KickOff));
    }

    [Fact]
    public async Task A_game_that_does_not_exist_is_nothing_to_send()
    {
        await SeedGameAsync();
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        Assert.Null(await _audience.ForAsync(9999, LiveMatchEvent.Goal));
    }

    [Fact]
    public async Task A_finished_match_says_so_so_the_tap_lands_on_the_result()
    {
        var game = await SeedGameAsync();
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        var match = await _audience.ForAsync(game.Id, LiveMatchEvent.FullTime);

        Assert.True(match!.IsFinished);
    }

    [Fact]
    public async Task Endpoints_the_push_service_has_given_up_on_are_dropped()
    {
        await SeedGameAsync();
        await _push.SubscribeAsync(Endpoint, Key, Auth, "nl");

        await _audience.DropAsync([Endpoint]);

        Assert.Empty(await Read().PushSubscriptions.IgnoreQueryFilters().ToListAsync());
    }
}
