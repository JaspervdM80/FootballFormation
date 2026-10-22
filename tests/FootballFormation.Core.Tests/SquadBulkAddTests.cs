namespace FootballFormation.Core.Tests;

/// Filling a new team's squad in one go: a pasted list of names, or several existing players picked at once.
public class SquadBulkAddTests : ServiceTestBase
{
    [Fact]
    public async Task A_pasted_list_creates_the_players_and_puts_them_in_the_squad()
    {
        var season = await SeedSeasonAsync();

        var result = await Squads.AddPastedListAsync(season.Id, "7 Sanne de Vries\nLotte\n12 Femke Jansen");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Created.Count);
        Assert.Equal(3, result.Value.Added);

        await using var db = Read();
        var sanne = db.Players.Single(p => p.FirstName == "Sanne");
        Assert.Equal(("de Vries", 7, CurrentTeam.ClubId!.Value), (sanne.Surname!, sanne.ShirtNumber!.Value, sanne.ClubId));
        Assert.Null(db.Players.Single(p => p.FirstName == "Lotte").Surname);
        Assert.Equal(3, db.SeasonSquadMembers.Count(m => m.SeasonId == season.Id && m.TeamId == season.TeamId));
    }

    [Fact]
    public async Task A_player_from_this_teams_earlier_season_is_added_rather_than_created_twice()
    {
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(lastSeason.Id, players[0].Id);

        var result = await Squads.AddPastedListAsync(season.Id, "p1\nLotte");

        Assert.Equal(players[0].Id, Assert.Single(result.Value!.AddedFromEarlierSeason).Id);
        Assert.Single(result.Value.Created);
        Assert.Equal(2, Read().Players.Count());
        Assert.Equal(2, Read().SeasonSquadMembers.Count(m => m.SeasonId == season.Id));
    }

    [Fact]
    public async Task A_reused_player_keeps_her_own_shirt_number()
    {
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(lastSeason.Id, players[0].Id);

        await Squads.AddPastedListAsync(season.Id, "42 P1");

        Assert.Equal(1, Read().Players.Single(p => p.Id == players[0].Id).ShirtNumber);
    }

    [Fact]
    public async Task A_name_already_in_the_squad_is_skipped_and_reported()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id, isGuest: true);

        var result = await Squads.AddPastedListAsync(season.Id, "P1\nLotte");

        Assert.Equal(players[0].Id, Assert.Single(result.Value!.AlreadyInSquad).Id);
        Assert.Equal(1, result.Value.Added);
        Assert.Equal(2, Read().SeasonSquadMembers.Count());
        // Skipped means untouched: her guest flag is not reset by being pasted again.
        Assert.True(Read().SeasonSquadMembers.Single(m => m.PlayerId == players[0].Id).IsGuest);
    }

    [Fact]
    public async Task An_archived_player_from_an_earlier_season_is_not_brought_back_by_a_pasted_name()
    {
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(lastSeason.Id, players[0].Id);
        await Players.SetArchivedAsync(players[0].Id, true);

        var result = await Squads.AddPastedListAsync(season.Id, "P1");

        Assert.Single(result.Value!.Created);
        Assert.Empty(result.Value.AddedFromEarlierSeason);
    }

    [Fact]
    public async Task A_player_on_file_who_never_was_in_this_teams_squads_is_not_reused()
    {
        var season = await SeedSeasonAsync();
        await SeedPlayersAsync(1);

        var result = await Squads.AddPastedListAsync(season.Id, "P1");

        Assert.Single(result.Value!.Created);
        Assert.Empty(result.Value.AddedFromEarlierSeason);
    }

    [Fact]
    public async Task A_player_from_another_team_at_the_same_club_is_not_reused()
    {
        SeedTeam("GJS", "MO17-1");
        var theirSeason = await SeedSeasonAsync();
        var theirs = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(theirSeason.Id, theirs[0].Id);
        SeedTeam();
        var season = await SeedSeasonAsync();

        Assert.Empty((await Squads.GetFormerMembersAsync(season.Id)).Value!);

        var result = await Squads.AddPastedListAsync(season.Id, "P1");

        Assert.Single(result.Value!.Created);
        Assert.Equal(2, Read().Players.Count(p => p.FirstName == "P1"));
    }

    [Fact]
    public async Task Another_clubs_player_of_the_same_name_is_never_reused()
    {
        SeedTeam("Club B", "MO17-1");
        await SeedPlayersAsync(1);
        SeedTeam();
        var season = await SeedSeasonAsync();

        var result = await Squads.AddPastedListAsync(season.Id, "P1");

        Assert.Single(result.Value!.Created);
        Assert.Equal(2, Read().Players.IgnoreQueryFilters().Count(p => p.FirstName == "P1"));
    }

    [Fact]
    public async Task The_earlier_players_are_this_teams_former_members_outside_the_squad()
    {
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        await Squads.AddMemberAsync(lastSeason.Id, players[0].Id);
        await Squads.AddMemberAsync(lastSeason.Id, players[1].Id);
        await Squads.AddMemberAsync(season.Id, players[1].Id);

        var earlier = await Squads.GetFormerMembersAsync(season.Id);

        Assert.Equal([players[0].Id], earlier.Value!.Select(p => p.Id));
    }

    [Fact]
    public async Task One_flagged_line_refuses_the_whole_list()
    {
        var season = await SeedSeasonAsync();

        var result = await Squads.AddPastedListAsync(season.Id, "7 Sanne\n100 Lotte");

        Assert.True(result.IsFailure);
        Assert.Equal("Line {0} of the list cannot be added", result.ErrorKey);
        Assert.Equal([2], result.ErrorArgs);
        Assert.Empty(Read().Players);
    }

    [Fact]
    public async Task An_empty_list_or_a_missing_season_is_refused()
    {
        var season = await SeedSeasonAsync();

        Assert.True((await Squads.AddPastedListAsync(season.Id, "\n  \n")).IsFailure);
        Assert.True((await Squads.AddPastedListAsync(9999, "Sanne")).IsFailure);
        Assert.Empty(Read().Players);
    }

    [Fact]
    public async Task A_pasted_list_invalidates_the_statistics_cache()
    {
        var season = await SeedSeasonAsync();
        var generation = StatsCache.Generation;

        await Squads.AddPastedListAsync(season.Id, "Sanne");

        Assert.NotEqual(generation, StatsCache.Generation);
    }

    [Fact]
    public async Task Several_existing_players_join_the_squad_in_one_go()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);

        var result = await Squads.AddMembersAsync(season.Id, [players[0].Id, players[2].Id], isGuest: true, isInjured: true);

        Assert.Equal(2, result.Value);
        var members = Read().SeasonSquadMembers.ToList();
        Assert.Equal([players[0].Id, players[2].Id], members.Select(m => m.PlayerId).Order());
        Assert.All(members, m => Assert.True(m.IsGuest && m.IsInjured && m.InjuredSince == Now.Date));
    }

    [Fact]
    public async Task Picking_someone_already_in_the_squad_refuses_the_whole_pick()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        await Squads.AddMemberAsync(season.Id, players[1].Id);

        var result = await Squads.AddMembersAsync(season.Id, [players[0].Id, players[1].Id]);

        Assert.True(result.IsFailure);
        Assert.Single(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task Picking_nobody_an_unknown_player_or_an_unknown_season_is_refused()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        Assert.True((await Squads.AddMembersAsync(season.Id, [])).IsFailure);
        Assert.True((await Squads.AddMembersAsync(season.Id, [players[0].Id, 9999])).IsFailure);
        Assert.True((await Squads.AddMembersAsync(9999, [players[0].Id])).IsFailure);
        Assert.Empty(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task Another_clubs_player_cannot_be_picked_by_id()
    {
        SeedTeam("Club B", "MO17-1");
        var theirs = await SeedPlayersAsync(1);
        SeedTeam();
        var season = await SeedSeasonAsync();

        var result = await Squads.AddMembersAsync(season.Id, [theirs[0].Id]);

        Assert.True(result.IsFailure);
        Assert.Empty(Read().SeasonSquadMembers);
    }
}
