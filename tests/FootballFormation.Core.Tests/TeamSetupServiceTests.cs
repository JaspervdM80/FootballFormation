namespace FootballFormation.Core.Tests;

public class TeamSetupServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_new_team_has_every_step_to_do_and_shows_the_checklist()
    {
        await SeedSeasonAsync();

        var setup = (await TeamSetup.GetAsync()).Value!;

        Assert.Equal(0, setup.DoneCount);
        Assert.True(setup.Shows);
    }

    [Fact]
    public async Task Each_step_is_ticked_off_by_its_own_data()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        var prefs = (await Preferences.GetAsync(season.Id)).Value!;
        Assert.False((await TeamSetup.GetAsync()).Value!.HasMatchSettings);

        await Preferences.SaveMatchDefaultsAsync(prefs);
        Db.SeasonSquadMembers.Add(new SeasonSquadMember { SeasonId = season.Id, TeamId = season.TeamId, PlayerId = players[0].Id });
        Db.Trainings.Add(new Training { SeasonId = season.Id, TeamId = season.TeamId, Date = Now.Date });
        Db.Games.Add(GameIn(season));
        await Db.SaveChangesAsync();

        var setup = (await TeamSetup.GetAsync()).Value!;

        Assert.True(setup.HasMatchSettings);
        Assert.True(setup.HasSquad);
        Assert.True(setup.HasTrainings);
        Assert.True(setup.HasGames);
        Assert.True(setup.IsComplete);
        Assert.False(setup.Shows);
    }

    [Fact]
    public async Task Another_teams_data_does_not_tick_anything_off()
    {
        SeedTeam("GJS", "MO17-1");
        var other = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Preferences.SaveMatchDefaultsAsync((await Preferences.GetAsync(other.Id)).Value!);
        Db.SeasonSquadMembers.Add(new SeasonSquadMember { SeasonId = other.Id, TeamId = other.TeamId, PlayerId = players[0].Id });
        Db.Trainings.Add(new Training { SeasonId = other.Id, TeamId = other.TeamId, Date = Now.Date });
        Db.Games.Add(GameIn(other));
        await Db.SaveChangesAsync();

        SeedTeam("GJS", "MO15-2");
        await SeedSeasonAsync();

        var setup = (await TeamSetup.GetAsync()).Value!;

        Assert.Equal(0, setup.DoneCount);
        Assert.True(setup.Shows);
    }

    [Fact]
    public async Task Hiding_the_checklist_keeps_it_hidden_with_steps_still_to_do()
    {
        await SeedSeasonAsync();

        var hidden = await TeamSetup.HideAsync();
        var setup = (await TeamSetup.GetAsync()).Value!;

        Assert.True(hidden.IsSuccess);
        Assert.False(setup.IsComplete);
        Assert.False(setup.Shows);
    }

    [Fact]
    public async Task Hiding_one_teams_checklist_leaves_another_teams_alone()
    {
        SeedTeam("GJS", "MO17-1");
        await TeamSetup.HideAsync();

        SeedTeam("GJS", "MO15-2");

        Assert.True((await TeamSetup.GetAsync()).Value!.Shows);
    }

    [Fact]
    public async Task A_visitor_can_neither_read_nor_hide_the_checklist()
    {
        var team = SeedTeam();
        CurrentUser.IsAdmin = false;
        CurrentUser.IsApplicationAdmin = false;

        Assert.True((await TeamSetup.GetAsync()).IsFailure);
        Assert.True((await TeamSetup.HideAsync()).IsFailure);
        Assert.False(Read().Teams.Single(t => t.Id == team.Id).SetupChecklistHidden);
    }

    private static Game GameIn(Season season)
    {
        var game = TestData.Game(id: 0, seasonId: season.Id, date: Now.Date);
        game.TeamId = season.TeamId;
        return game;
    }
}
