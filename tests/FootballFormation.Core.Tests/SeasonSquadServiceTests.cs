namespace FootballFormation.Core.Tests;

/// The rule worth pinning is the refusal: removing someone who already played would rewrite that season's statistics unasked.
public class SeasonSquadServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_player_who_has_not_played_can_be_removed()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(season.Id, [players[0].Id]);

        Assert.True((await Squads.RemoveMemberAsync(season.Id, players[0].Id)).IsSuccess);
        Assert.Empty(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task A_player_who_has_already_played_cannot_be_removed()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(season.Id, [players[0].Id]);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);

        var result = await Squads.RemoveMemberAsync(season.Id, players[0].Id);

        Assert.True(result.IsFailure);
        Assert.Single(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task A_player_with_a_goal_this_season_cannot_be_removed()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(season.Id, [players[0].Id]);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id))).Value!;
        await Games.AddGoalAsync(new GameGoal { GameId = game.Id, ScorerId = players[0].Id, Minute = 5 });

        Assert.True((await Squads.RemoveMemberAsync(season.Id, players[0].Id)).IsFailure);
    }

    [Fact]
    public async Task Playing_in_another_season_does_not_block_removal_from_this_one()
    {
        var lastSeason = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var thisSeason = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(1);

        await Squads.AddMembersAsync(lastSeason.Id, [players[0].Id]);
        await Squads.AddMembersAsync(thisSeason.Id, [players[0].Id]);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: lastSeason.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);

        // The refusal is per season: last season's minutes are not this season's statistics.
        Assert.True((await Squads.RemoveMemberAsync(thisSeason.Id, players[0].Id)).IsSuccess);
        Assert.True((await Squads.RemoveMemberAsync(lastSeason.Id, players[0].Id)).IsFailure);
    }

    [Fact]
    public async Task Every_membership_write_refuses_a_non_member_in_the_same_words()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        var removed = await Squads.RemoveMemberAsync(season.Id, players[0].Id);
        var madeGuest = await Squads.SetGuestAsync(season.Id, players[0].Id, isGuest: true);
        var markedInjured = await Squads.SetInjuredAsync(season.Id, players[0].Id, true);

        // Same load, same refusal — and the message is the resource key, so two spellings would be
        // one translated string and one English one.
        Assert.True(removed.IsFailure);
        Assert.Equal(removed.ErrorKey, madeGuest.ErrorKey);
        Assert.Equal(removed.ErrorKey, markedInjured.ErrorKey);
    }

    [Fact]
    public async Task Guest_status_can_be_switched_both_ways()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(season.Id, [players[0].Id], isGuest: true);

        Assert.True(Read().SeasonSquadMembers.Single().IsGuest);

        await Squads.SetGuestAsync(season.Id, players[0].Id, false);
        Assert.False(Read().SeasonSquadMembers.Single().IsGuest);
    }

    [Fact]
    public async Task Injury_status_can_be_switched_both_ways()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(season.Id, [players[0].Id], isInjured: true);

        Assert.True(Read().SeasonSquadMembers.Single().IsInjured);

        await Squads.SetInjuredAsync(season.Id, players[0].Id, false);
        Assert.False(Read().SeasonSquadMembers.Single().IsInjured);
    }

    [Fact]
    public async Task An_injury_is_dated_when_it_is_flagged_and_the_date_goes_when_it_heals()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        await Squads.AddMembersAsync(season.Id, [players[0].Id], isInjured: true);
        await Squads.AddMembersAsync(season.Id, [players[1].Id]);

        Assert.Equal(Now.Date, Read().SeasonSquadMembers.Single(m => m.PlayerId == players[0].Id).InjuredSince);
        Assert.Null(Read().SeasonSquadMembers.Single(m => m.PlayerId == players[1].Id).InjuredSince);

        Time.Advance(TimeSpan.FromDays(7));
        await Squads.SetInjuredAsync(season.Id, players[1].Id, true);
        Assert.Equal(Now.Date.AddDays(7), Read().SeasonSquadMembers.Single(m => m.PlayerId == players[1].Id).InjuredSince);

        // Re-saving an injury that is already on must not move its date forward, or the sessions it has already stamped stop matching
        // the flag that stamped them.
        Time.Advance(TimeSpan.FromDays(7));
        await Squads.SetInjuredAsync(season.Id, players[1].Id, true);
        Assert.Equal(Now.Date.AddDays(7), Read().SeasonSquadMembers.Single(m => m.PlayerId == players[1].Id).InjuredSince);

        await Squads.SetInjuredAsync(season.Id, players[0].Id, false);
        Assert.Null(Read().SeasonSquadMembers.Single(m => m.PlayerId == players[0].Id).InjuredSince);
    }

    [Fact]
    public async Task Copying_a_squad_forward_does_not_carry_injury_status()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(1);
        await Squads.AddMembersAsync(last.Id, [players[0].Id], isInjured: true);

        await Squads.CopyFromAsync(last.Id, next.Id);

        // Injuries are expected to have healed by the time next season's squad is set up, unlike
        // guest status, which copying forward does preserve.
        Assert.False(Read().SeasonSquadMembers.Single(m => m.SeasonId == next.Id).IsInjured);
    }

    [Fact]
    public async Task Copying_a_squad_forward_preserves_guest_status_and_skips_duplicates()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(3);

        await Squads.AddMembersAsync(last.Id, [players[0].Id]);
        await Squads.AddMembersAsync(last.Id, [players[1].Id], isGuest: true);
        await Squads.AddMembersAsync(last.Id, [players[2].Id]);
        await Squads.AddMembersAsync(next.Id, [players[0].Id]);

        var copied = await Squads.CopyFromAsync(last.Id, next.Id);

        Assert.Equal(2, copied.Value);
        var squad = Read().SeasonSquadMembers.Where(m => m.SeasonId == next.Id).ToList();
        Assert.Equal(3, squad.Count);
        Assert.True(squad.Single(m => m.PlayerId == players[1].Id).IsGuest);

        // Idempotent: running it again adds nothing, so the settings button is safe to press twice.
        Assert.Equal(0, (await Squads.CopyFromAsync(last.Id, next.Id)).Value);
    }

    [Fact]
    public async Task A_squad_cannot_be_copied_onto_itself_or_from_an_empty_season()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);

        Assert.True((await Squads.CopyFromAsync(next.Id, next.Id)).IsFailure);
        Assert.True((await Squads.CopyFromAsync(last.Id, next.Id)).IsFailure);
    }

    [Fact]
    public async Task An_archived_player_is_not_offered_for_a_squad()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        await Squads.AddMembersAsync(last.Id, [players[0].Id]);
        await Squads.AddMembersAsync(last.Id, [players[1].Id]);
        await Players.SetArchivedAsync(players[0].Id, true);

        var offered = await Squads.GetFormerMembersAsync(season.Id);

        // Offering someone who has left is what would make archiving them pointless.
        Assert.Equal([players[1].Id], offered.Value!.Select(p => p.Id));
    }

    [Fact]
    public async Task Copying_a_squad_forward_leaves_the_archived_players_behind()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(2);
        await Squads.AddMembersAsync(last.Id, [players[0].Id]);
        await Squads.AddMembersAsync(last.Id, [players[1].Id]);

        await Players.SetArchivedAsync(players[1].Id, true);

        var copied = await Squads.CopyFromAsync(last.Id, next.Id);

        // Setting up a new season is the one moment that would otherwise undo the archiving, every
        // year. The player stays in last season's squad, where they belong.
        Assert.Equal(1, copied.Value);
        Assert.Equal([players[0].Id],
            Read().SeasonSquadMembers.Where(m => m.SeasonId == next.Id).Select(m => m.PlayerId));
        Assert.Equal(2, Read().SeasonSquadMembers.Count(m => m.SeasonId == last.Id));
    }
}
