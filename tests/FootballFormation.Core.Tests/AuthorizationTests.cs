using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The services refuse to write for a caller who is not an admin.
/// <para>
/// The app already gates every mutating control behind an <c>AuthorizeView</c>, so in practice
/// nobody reaches these methods unauthorized today. That is exactly why these tests exist: the
/// guard is the layer that keeps holding when the markup stops — a new page, a minimal API, a
/// control that outgrows its wrapper — and a silent regression there would look like nothing.
/// </para>
/// </summary>
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
    public async Task An_anonymous_caller_cannot_drive_a_live_match()
    {
        var season = await SeedSeasonAsync();
        var game = await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id));

        CurrentUser.IsAdmin = false;

        var result = await Live.StartMatchAsync(game.Value!.Id);

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
