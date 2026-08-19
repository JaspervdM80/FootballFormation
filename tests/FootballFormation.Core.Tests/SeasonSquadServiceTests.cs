using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// Squad membership per season. The rule worth pinning down is the refusal: removing someone who
/// already played would rewrite that season's statistics without anyone asking for it.
/// </summary>
public class SeasonSquadServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_player_can_be_added_once()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        Assert.True((await Squads.AddMemberAsync(season.Id, players[0].Id)).IsSuccess);

        var again = await Squads.AddMemberAsync(season.Id, players[0].Id);

        // Refused here with a readable message rather than surfacing the unique index as a raw
        // DbUpdateException.
        Assert.True(again.IsFailure);
        Assert.Single(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task Adding_to_a_season_or_player_that_does_not_exist_is_refused()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        Assert.True((await Squads.AddMemberAsync(9999, players[0].Id)).IsFailure);
        Assert.True((await Squads.AddMemberAsync(season.Id, 9999)).IsFailure);
        Assert.Empty(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task A_player_who_has_not_played_can_be_removed()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id);

        Assert.True((await Squads.RemoveMemberAsync(season.Id, players[0].Id)).IsSuccess);
        Assert.Empty(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task A_player_who_has_already_played_cannot_be_removed()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id);

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
        await Squads.AddMemberAsync(season.Id, players[0].Id);

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

        await Squads.AddMemberAsync(lastSeason.Id, players[0].Id);
        await Squads.AddMemberAsync(thisSeason.Id, players[0].Id);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: lastSeason.Id))).Value!;
        var period = Read().GamePeriods.First(p => p.GameId == game.Id);
        await Games.SavePeriodLineupAsync(period.Id, [TestData.Starter(players[0].Id, PlayerPosition.GK, slot: 0)]);

        // The refusal is per season: last season's minutes are not this season's statistics.
        Assert.True((await Squads.RemoveMemberAsync(thisSeason.Id, players[0].Id)).IsSuccess);
        Assert.True((await Squads.RemoveMemberAsync(lastSeason.Id, players[0].Id)).IsFailure);
    }

    [Fact]
    public async Task Both_membership_writes_refuse_a_non_member_in_the_same_words()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        var removed = await Squads.RemoveMemberAsync(season.Id, players[0].Id);
        var madeGuest = await Squads.SetGuestAsync(season.Id, players[0].Id, isGuest: true);

        // Same load, same refusal — and the message is the resource key, so two spellings would be
        // one translated string and one English one.
        Assert.True(removed.IsFailure);
        Assert.Equal(removed.ErrorKey, madeGuest.ErrorKey);
    }

    [Fact]
    public async Task Guest_status_can_be_switched_both_ways()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id, isGuest: true);

        Assert.True(Read().SeasonSquadMembers.Single().IsGuest);

        await Squads.SetGuestAsync(season.Id, players[0].Id, false);
        Assert.False(Read().SeasonSquadMembers.Single().IsGuest);
    }

    [Fact]
    public async Task Marking_a_non_member_injured_is_refused_like_the_other_membership_writes()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);

        var removed = await Squads.RemoveMemberAsync(season.Id, players[0].Id);
        var markedInjured = await Squads.SetInjuredAsync(season.Id, players[0].Id, true);

        Assert.True(markedInjured.IsFailure);
        Assert.Equal(removed.ErrorKey, markedInjured.ErrorKey);
    }

    [Fact]
    public async Task Injury_status_can_be_switched_both_ways()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(season.Id, players[0].Id, isInjured: true);

        Assert.True(Read().SeasonSquadMembers.Single().IsInjured);

        await Squads.SetInjuredAsync(season.Id, players[0].Id, false);
        Assert.False(Read().SeasonSquadMembers.Single().IsInjured);
    }

    [Fact]
    public async Task Copying_a_squad_forward_does_not_carry_injury_status()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(1);
        await Squads.AddMemberAsync(last.Id, players[0].Id, isInjured: true);

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

        await Squads.AddMemberAsync(last.Id, players[0].Id);
        await Squads.AddMemberAsync(last.Id, players[1].Id, isGuest: true);
        await Squads.AddMemberAsync(last.Id, players[2].Id);
        await Squads.AddMemberAsync(next.Id, players[0].Id);

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
    public async Task Non_members_are_the_players_not_yet_in_the_squad()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);
        await Squads.AddMemberAsync(season.Id, players[1].Id);

        var outside = await Squads.GetNonMembersAsync(season.Id);

        Assert.Equal([players[0].Id, players[2].Id], outside.Value!.Select(p => p.Id));
    }

    [Fact]
    public async Task An_archived_player_is_not_offered_for_a_squad()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        await Players.SetArchivedAsync(players[0].Id, true);

        var outside = await Squads.GetNonMembersAsync(season.Id);

        // Offering someone who has left is what would make archiving them pointless.
        Assert.Equal([players[1].Id], outside.Value!.Select(p => p.Id));
    }

    [Fact]
    public async Task Copying_a_squad_forward_leaves_the_archived_players_behind()
    {
        var last = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Now);
        var players = await SeedPlayersAsync(2);
        await Squads.AddMemberAsync(last.Id, players[0].Id);
        await Squads.AddMemberAsync(last.Id, players[1].Id);

        await Players.SetArchivedAsync(players[1].Id, true);

        var copied = await Squads.CopyFromAsync(last.Id, next.Id);

        // Setting up a new season is the one moment that would otherwise undo the archiving, every
        // year. The player stays in last season's squad, where they belong.
        Assert.Equal(1, copied.Value);
        Assert.Equal([players[0].Id],
            Read().SeasonSquadMembers.Where(m => m.SeasonId == next.Id).Select(m => m.PlayerId));
        Assert.Equal(2, Read().SeasonSquadMembers.Count(m => m.SeasonId == last.Id));
    }

    [Fact]
    public async Task The_previous_season_is_the_one_immediately_before()
    {
        var oldest = await SeedSeasonAsync(covering: Now.AddYears(-2), isCurrent: false);
        var middle = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        var current = await SeedSeasonAsync(covering: Now);

        Assert.Equal(middle.Id, (await Squads.FindPreviousSeasonAsync(current.Id)).Value!.Id);

        // A null value with a successful result is the normal "there is no earlier season" answer,
        // not an error — the copy-forward offer just does not appear.
        var none = await Squads.FindPreviousSeasonAsync(oldest.Id);
        Assert.True(none.IsSuccess);
        Assert.Null(none.Value);
    }
}
