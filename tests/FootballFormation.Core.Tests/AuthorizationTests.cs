namespace FootballFormation.Core.Tests;

/// Nobody reaches these methods unauthorized today, which is exactly why the tests exist: this guard is the layer that keeps holding
/// when the markup stops, and a silent regression in it would look like nothing at all.
public class AuthorizationTests : ServiceTestBase
{
    [Fact]
    public async Task Reads_stay_open_to_everyone()
    {
        var season = await SeedSeasonAsync();
        await SeedPlayersAsync(3);

        // Parents and players read the site without signing in — the season, the squad, the games
        // and the statistics are all public, and only changing them is not.
        CurrentUser.IsAdmin = false;

        Assert.True((await Players.GetAllAsync()).IsSuccess);
        Assert.True((await Seasons.GetAllAsync()).IsSuccess);
        Assert.True((await Games.GetAllAsync(season.Id)).IsSuccess);
        Assert.True((await Squads.GetSquadAsync(season.Id)).IsSuccess);
        Assert.True((await Preferences.GetAsync(season.Id)).IsSuccess);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_a_player()
    {
        CurrentUser.IsAdmin = false;

        var result = await Players.CreateAsync(new Player { FirstName = "Intruder" });

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().Players);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_delete_a_game()
    {
        var season = await SeedSeasonAsync();
        var game = await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id));

        CurrentUser.IsAdmin = false;

        var result = await Games.DeleteAsync(game.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Single(Read().Games);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_change_the_squad()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        CurrentUser.IsAdmin = false;

        var result = await Squads.AddMemberAsync(season.Id, players[0].Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_archive_a_player()
    {
        var players = await SeedPlayersAsync(1);

        CurrentUser.IsAdmin = false;

        var result = await Players.SetArchivedAsync(players[0].Id, true);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.False(Read().Players.Single().IsArchived);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_drive_a_live_match()
    {
        var season = await SeedSeasonAsync();
        var game = await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id));

        CurrentUser.IsAdmin = false;

        var result = await MatchClock.StartMatchAsync(game.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Equal(MatchState.NotStarted, Read().Games.Single().MatchState);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_a_user()
    {
        CurrentUser.IsAdmin = false;

        var result = await Users.CreateAsync("Intruder", "intruder", "long-enough-password", UserRole.Admin);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().Users);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_write_a_comment()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;

        CurrentUser.IsAdmin = false;

        var result = await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Intruder" });

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().GameComments);
    }

    [Fact]
    public async Task An_anonymous_caller_asking_for_private_comments_gets_only_the_public_ones()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Public", IsPublic = true });
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Private", IsPublic = false });

        CurrentUser.IsAdmin = false;

        // Passing includePrivate: true is not the same as being allowed to: the result page prerenders, so a private body reaching the
        // query at all would ship in the markup even if the page hid the row.
        var comments = await Games.GetCommentsAsync(game.Id, includePrivate: true);

        Assert.True(comments.IsSuccess);
        Assert.Equal("Public", Assert.Single(comments.Value!).Body);
    }

    [Fact]
    public async Task An_admin_sees_the_private_comments()
    {
        var season = await SeedSeasonAsync();
        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Public", IsPublic = true });
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Private", IsPublic = false });

        var comments = await Games.GetCommentsAsync(game.Id, includePrivate: true);

        Assert.Equal(2, comments.Value!.Count);
    }

    [Fact]
    public async Task The_refusal_names_the_action_so_the_message_can_be_translated()
    {
        CurrentUser.IsAdmin = false;

        var result = await Players.CreateAsync(new Player { FirstName = "Intruder" });

        // Same shape as the unexpected-failure wrapper: one argument, an English action phrase that
        // is itself a resource key. UiFeedback.Translate looks both parts up — see its comment.
        var action = Assert.IsType<string>(Assert.Single(result.ErrorArgs));
        Assert.Equal("create player", action);
    }
}
