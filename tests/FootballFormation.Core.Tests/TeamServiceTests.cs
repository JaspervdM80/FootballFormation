namespace FootballFormation.Core.Tests;

/// The club sits above the season, so the rules worth pinning are the ones that stop a deployment losing the thing every other page
/// hangs off: the last team, and a club with teams still on it.
public class TeamServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_team_belongs_to_a_club()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;

        var team = await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO15-2" });

        Assert.True(team.IsSuccess);
        Assert.Equal("GJS MO15-2", (await TeamsAndClubs.GetTeamsAsync()).Value!.Single().FullName);
    }

    [Fact]
    public async Task A_club_starts_on_the_default_theme()
    {
        var club = await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" });

        Assert.Equal(Club.DefaultTheme, club.Value!.ThemeName);
    }

    [Fact]
    public async Task A_team_cannot_be_created_for_a_club_that_does_not_exist()
    {
        var team = await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = 404, Name = "MO15-2" });

        Assert.True(team.IsFailure);
        Assert.Empty(Read().Teams);
    }

    [Fact]
    public async Task Two_clubs_may_each_have_an_MO15_2_but_one_club_may_not_have_two()
    {
        var gjs = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        var other = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "SV Zwaluwen" })).Value!;

        await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = gjs.Id, Name = "MO15-2" });

        Assert.True((await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = other.Id, Name = "MO15-2" })).IsSuccess);
        Assert.True((await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = gjs.Id, Name = "MO15-2" })).IsFailure);
        Assert.Equal(2, Read().Teams.Count());
    }

    [Fact]
    public async Task A_club_name_is_taken_only_once()
    {
        await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" });

        var duplicate = await TeamsAndClubs.CreateClubAsync(new Club { Name = " GJS " });

        Assert.True(duplicate.IsFailure);
        Assert.Single(Read().Clubs);
    }

    [Fact]
    public async Task A_blank_name_is_refused_for_both()
    {
        var club = await TeamsAndClubs.CreateClubAsync(new Club { Name = "   " });
        var created = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        var team = await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = created.Id, Name = "   " });

        Assert.True(club.IsFailure);
        Assert.True(team.IsFailure);
    }

    [Fact]
    public async Task An_empty_logo_is_stored_as_nothing_rather_than_as_a_blank_path()
    {
        var club = await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS", LogoUrl = "   " });

        // The page falls back to the theme's crest on null; a blank string would render a broken image instead.
        Assert.Null(club.Value!.LogoUrl);
    }

    [Fact]
    public async Task A_logo_outside_the_app_is_refused()
    {
        var absolute = await TeamsAndClubs.CreateClubAsync(
            new Club { Name = "GJS", LogoUrl = "https://images.example.com/crest.png" });

        // The crest renders into an img on every page for every visitor, so an absolute URL would have the whole audience fetching a
        // third party. Protocol-relative is the same thing wearing a different hat.
        var protocolRelative = await TeamsAndClubs.CreateClubAsync(
            new Club { Name = "GJS", LogoUrl = "//images.example.com/crest.png" });

        var script = await TeamsAndClubs.CreateClubAsync(
            new Club { Name = "GJS", LogoUrl = "javascript:alert(1)" });

        Assert.True(absolute.IsFailure);
        Assert.True(protocolRelative.IsFailure);
        Assert.True(script.IsFailure);
        Assert.Empty(Read().Clubs);
    }

    [Fact]
    public async Task A_logo_inside_the_app_is_kept()
    {
        var club = await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS", LogoUrl = "icons/crest.png" });

        Assert.Equal("icons/crest.png", club.Value!.LogoUrl);
    }

    [Fact]
    public async Task A_club_can_be_renamed_and_re_themed()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;

        var updated = await TeamsAndClubs.UpdateClubAsync(
            new Club { Id = club.Id, Name = " GJS Gorinchem ", LogoUrl = " icons/crest.png ", ThemeName = "GJS" });

        Assert.True(updated.IsSuccess);
        var stored = Read().Clubs.Single();
        Assert.Equal("GJS Gorinchem", stored.Name);
        Assert.Equal("icons/crest.png", stored.LogoUrl);
    }

    [Fact]
    public async Task A_rename_cannot_take_a_name_another_club_already_has()
    {
        var gjs = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        await TeamsAndClubs.CreateClubAsync(new Club { Name = "SV Zwaluwen" });

        var clash = await TeamsAndClubs.UpdateClubAsync(new Club { Id = gjs.Id, Name = "SV Zwaluwen" });

        Assert.True(clash.IsFailure);
        Assert.Equal("GJS", Read().Clubs.Single(c => c.Id == gjs.Id).Name);
    }

    [Fact]
    public async Task A_theme_a_club_was_saved_without_falls_back_to_the_default()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;

        await TeamsAndClubs.UpdateClubAsync(new Club { Id = club.Id, Name = "GJS", ThemeName = "  " });

        Assert.Equal(Club.DefaultTheme, Read().Clubs.Single().ThemeName);
    }

    [Fact]
    public async Task A_team_can_be_renamed_and_moved_to_another_club()
    {
        var gjs = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        var other = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "SV Zwaluwen" })).Value!;
        var team = (await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = gjs.Id, Name = "MO15-2" })).Value!;

        var updated = await TeamsAndClubs.UpdateTeamAsync(
            new Team { Id = team.Id, ClubId = other.Id, Name = " MO17-1 " });

        Assert.True(updated.IsSuccess);
        Assert.Equal("SV Zwaluwen MO17-1", (await TeamsAndClubs.GetTeamsAsync()).Value!.Single().FullName);
    }

    [Fact]
    public async Task A_team_rename_cannot_collide_inside_its_own_club()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        var team = (await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO15-2" })).Value!;
        await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO17-1" });

        var clash = await TeamsAndClubs.UpdateTeamAsync(new Team { Id = team.Id, ClubId = club.Id, Name = "MO17-1" });

        Assert.True(clash.IsFailure);
        Assert.Equal("MO15-2", Read().Teams.Single(t => t.Id == team.Id).Name);
    }

    [Fact]
    public async Task A_club_or_team_that_is_gone_is_reported_rather_than_thrown()
    {
        Assert.True((await TeamsAndClubs.UpdateClubAsync(new Club { Id = 404, Name = "GJS" })).IsFailure);
        Assert.True((await TeamsAndClubs.DeleteClubAsync(404)).IsFailure);
        Assert.True((await TeamsAndClubs.UpdateTeamAsync(new Team { Id = 404, ClubId = 1, Name = "MO15-2" })).IsFailure);
        Assert.True((await TeamsAndClubs.DeleteTeamAsync(404)).IsFailure);
    }

    [Fact]
    public async Task The_only_team_cannot_be_deleted()
    {
        await SeedTeamAsync();
        var team = (await TeamsAndClubs.GetTeamsAsync()).Value!.Single();

        // Every season, game and player hangs off the deployment rather than off a team, so removing this row would leave all of it
        // with nothing selecting it.
        var deleted = await TeamsAndClubs.DeleteTeamAsync(team.Id);

        Assert.True(deleted.IsFailure);
        Assert.Single(Read().Teams);
    }

    [Fact]
    public async Task The_team_the_app_is_showing_cannot_be_deleted_even_beside_another()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        var first = (await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO15-2" })).Value!;
        await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO17-1" });

        // Without this the app would silently rebrand: the title, the crest and the manifest would move to MO17-1 while every season
        // and game stayed exactly where it was.
        var deleted = await TeamsAndClubs.DeleteTeamAsync(first.Id);

        Assert.True(deleted.IsFailure);
        Assert.Equal("GJS MO15-2", (await TeamsAndClubs.GetCurrentAsync()).Value!.FullName);
    }

    [Fact]
    public async Task A_team_the_app_is_not_showing_can_be_deleted()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;
        await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO15-2" });
        var second = (await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO17-1" })).Value!;

        var deleted = await TeamsAndClubs.DeleteTeamAsync(second.Id);

        Assert.True(deleted.IsSuccess);
        Assert.Equal("GJS MO15-2", (await TeamsAndClubs.GetCurrentAsync()).Value!.FullName);
    }

    [Fact]
    public async Task A_club_with_teams_on_it_is_refused_rather_than_left_to_the_foreign_key()
    {
        await SeedTeamAsync();
        var club = (await TeamsAndClubs.GetClubsAsync()).Value!.Single();

        var deleted = await TeamsAndClubs.DeleteClubAsync(club.Id);

        Assert.True(deleted.IsFailure);
        Assert.Single(Read().Clubs);
    }

    [Fact]
    public async Task The_current_team_is_the_one_the_app_shows()
    {
        await SeedTeamAsync();

        var current = await TeamsAndClubs.GetCurrentAsync();

        // There is no picker yet, so this always answers with the seeded team — and with its club, which the app bar will need.
        Assert.Equal("MO15-2", current.Value!.Name);
        Assert.Equal("GJS", current.Value!.Club!.Name);
    }

    [Fact]
    public async Task There_is_no_current_team_before_anything_is_seeded()
    {
        var current = await TeamsAndClubs.GetCurrentAsync();

        Assert.True(current.IsSuccess);
        Assert.Null(current.Value);
    }

    [Fact]
    public async Task Seeding_does_nothing_once_a_club_exists()
    {
        await TeamsAndClubs.CreateClubAsync(new Club { Name = "Renamed" });

        await TeamsAndClubs.EnsureSeededAsync("GJS", "MO15-2");

        // Running the seeder over a live database must never resurrect the name it shipped with.
        Assert.Equal("Renamed", Read().Clubs.Single().Name);
        Assert.Empty(Read().Teams);
    }

    private Task SeedTeamAsync() => TeamsAndClubs.EnsureSeededAsync("GJS", "MO15-2");
}
