namespace FootballFormation.Core.Tests;

/// The squad's injury flag carries no date, so a match has exactly one chance to write down who it kept out: the moment it becomes part
/// of the record. Everything here is about that moment.
public class StandingInjuryTests : LiveMatchTestBase
{
    [Fact]
    public async Task Finishing_a_match_records_who_the_squad_had_injured()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        // players[3] is in the squad and in no lineup — the case the whole feature is about.
        await MarkInjuredAsync(game.SeasonId, players[3].Id);

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        var recorded = await ReloadAsync(game.Id);
        Assert.Equal([players[3].Id], recorded.InjuredPlayerIds);
    }

    [Fact]
    public async Task Somebody_who_took_part_is_not_recorded_as_having_missed_it()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        // Hurt after the whistle, or flagged the evening the result was typed. She was on the pitch, and the line-up outranks the flag.
        await MarkInjuredAsync(game.SeasonId, players[1].Id);

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task A_substitute_who_never_came_on_is_not_recorded_either()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        // Named on the bench, so she was there: bench time is availability, not absence.
        await MarkInjuredAsync(game.SeasonId, players[2].Id);

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task A_match_played_on_paper_records_it_when_the_score_goes_in()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);
        await MarkInjuredAsync(game.SeasonId, players[3].Id);

        // Never run live: a typed score is what settles it, so that is where the copy has to happen.
        Assert.True((await Games.SaveScoreAsync(game.Id, 2, 1)).IsSuccess);

        Assert.Equal([players[3].Id], (await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task Correcting_a_settled_score_leaves_the_record_as_it_was()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        Assert.True((await Games.SaveScoreAsync(game.Id, 2, 1)).IsSuccess);

        // Injured a fortnight later, so a corrected scoreline must not backdate the injury into a match already settled.
        await MarkInjuredAsync(game.SeasonId, players[3].Id);
        Assert.True((await Games.SaveScoreAsync(game.Id, 3, 1)).IsSuccess);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task Clearing_a_settled_score_and_typing_it_again_leaves_the_record_alone()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        // Written and empty is exactly what unwritten looks like, which is why the game carries a flag saying it was written.
        Assert.True((await Games.SaveScoreAsync(game.Id, 2, 1)).IsSuccess);

        await MarkInjuredAsync(game.SeasonId, players[3].Id);

        // The long way round to fixing a typo: empty the field, then type it again.
        Assert.True((await Games.SaveScoreAsync(game.Id, null, null)).IsSuccess);
        Assert.True((await Games.SaveScoreAsync(game.Id, 3, 1)).IsSuccess);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task Finishing_a_match_that_was_already_finished_does_not_rewrite_it()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);
        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);

        await MarkInjuredAsync(game.SeasonId, players[3].Id);
        await MatchClock.FinishMatchAsync(game.Id);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task An_unsettled_score_is_not_a_record_yet()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);
        await MarkInjuredAsync(game.SeasonId, players[3].Id);

        // Half a score is no score: the match is not complete, so there is nothing to freeze.
        Assert.True((await Games.SaveScoreAsync(game.Id, 2, null)).IsSuccess);
        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);

        Assert.True((await Games.SaveScoreAsync(game.Id, 2, 1)).IsSuccess);
        Assert.Equal([players[3].Id], (await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task Clearing_the_flag_afterwards_changes_nothing_that_already_happened()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);
        await MarkInjuredAsync(game.SeasonId, players[3].Id);

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        // Back in training, and the match still knows she missed it — the whole point of copying the flag rather than reading it.
        await MarkInjuredAsync(game.SeasonId, players[3].Id, injured: false);

        Assert.Equal([players[3].Id], (await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    [Fact]
    public async Task A_guest_is_left_out_of_it()
    {
        var game = await SeedGameAsync();
        var players = await PlayersAsync();
        await JoinSquadAsync(game.SeasonId, players);

        // Guests are out unless opted in, so recording one as injured would put a match in her availability she was never offered.
        var guest = await Db.SeasonSquadMembers
            .FirstAsync(m => m.SeasonId == game.SeasonId && m.PlayerId == players[3].Id);
        guest.IsGuest = true;
        guest.IsInjured = true;
        await Db.SaveChangesAsync();

        await MatchClock.StartMatchAsync(game.Id);
        await MatchClock.FinishMatchAsync(game.Id);

        Assert.Empty((await ReloadAsync(game.Id)).InjuredPlayerIds);
    }

    private async Task JoinSquadAsync(int seasonId, IEnumerable<Player> players)
    {
        Db.SeasonSquadMembers.AddRange(players.Select(p => new SeasonSquadMember
        {
            SeasonId = seasonId,
            PlayerId = p.Id
        }));
        await Db.SaveChangesAsync();
    }

    private async Task MarkInjuredAsync(int seasonId, int playerId, bool injured = true)
    {
        Db.ChangeTracker.Clear();
        var member = await Db.SeasonSquadMembers
            .FirstAsync(m => m.SeasonId == seasonId && m.PlayerId == playerId);

        member.IsInjured = injured;
        await Db.SaveChangesAsync();
    }
}
