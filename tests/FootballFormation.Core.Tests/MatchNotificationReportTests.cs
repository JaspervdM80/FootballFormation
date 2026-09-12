namespace FootballFormation.Core.Tests;

public class MatchNotificationReportTests : LiveMatchTestBase
{
    [Fact]
    public async Task A_goal_names_whoever_scored_it_and_the_minute_it_read()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(12));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var notification = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.Goal);

        Assert.Equal("P2", notification.ScorerName);
        Assert.Equal(13, notification.Minute!.Value.Minute);
    }

    /// The scoreline still moves, so a follower is still told — there is just nobody of ours to name.
    [Fact]
    public async Task An_opponent_goal_has_no_scorer_to_celebrate()
    {
        var game = await SeedGameAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, null, null, false, true);

        var notification = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.Goal);

        Assert.Null(notification.ScorerName);
        Assert.Equal(new VenueScore(0, 1), notification.Score);
    }

    [Fact]
    public async Task An_own_goal_is_not_credited_to_the_girl_who_put_it_in()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, players[0].Id, null, true, false);

        var notification = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.Goal);

        Assert.Null(notification.ScorerName);
    }

    [Fact]
    public async Task The_goal_reported_is_the_one_just_recorded_not_the_latest_on_the_clock()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(20));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        // A goal from earlier in the half, typed in after the fact — the one the coach just pressed, so the one to announce.
        Time.Advance(TimeSpan.FromSeconds(30));
        var late = await Goals.LogGoalAsync(game.Id, players[2].Id, null, false, false);
        await Goals.EditGoalAsync(late.Value!.Id, players[2].Id, null, false, 5);

        var notification = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.Goal);

        Assert.Equal("P3", notification.ScorerName);
    }

    [Fact]
    public async Task An_away_scoreline_reads_in_venue_order()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        game.IsHomeGame = false;
        Db.Update(game);
        await Db.SaveChangesAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var notification = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.Goal);

        Assert.False(notification.IsHomeGame);
        Assert.Equal(new VenueScore(0, 1), notification.Score);
    }

    [Fact]
    public async Task Kick_off_and_full_time_carry_no_goal_of_their_own()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();

        await MatchClock.StartMatchAsync(game.Id);
        Time.Advance(TimeSpan.FromMinutes(5));
        await Goals.LogGoalAsync(game.Id, players[1].Id, null, false, false);

        var kickOff = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.KickOff);
        var fullTime = MatchNotificationReport.Build(await LoadAsync(game.Id), LiveMatchEvent.FullTime);

        Assert.Null(kickOff.ScorerName);
        Assert.Null(kickOff.Minute);
        Assert.Null(fullTime.ScorerName);
        Assert.Equal(new VenueScore(1, 0), fullTime.Score);
    }

    /// A notification is read on a lock screen by whoever is holding the phone, and playing minutes are admin-only everywhere else in the
    /// app. Nothing here should be able to carry them, so the shape itself is asserted rather than any one message.
    [Fact]
    public void Nothing_in_a_notification_can_carry_playing_minutes()
    {
        var carried = typeof(MatchNotification).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Equal(
            ["Event", "Opponent", "IsHomeGame", "Score", "ScorerName", "Minute"],
            carried);
    }

    private async Task<Game> LoadAsync(int gameId)
    {
        Db.ChangeTracker.Clear();

        return await Db.Games
            .Include(g => g.Periods)
            .Include(g => g.Goals).ThenInclude(goal => goal.Scorer)
            .FirstAsync(g => g.Id == gameId);
    }
}
