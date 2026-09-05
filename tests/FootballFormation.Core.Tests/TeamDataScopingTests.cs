namespace FootballFormation.Core.Tests;

/// The read-side counterpart to <see cref="AuthorizationTests"/>. The write guard is one function asked in one place; a forgotten read
/// filter has no such choke point and would return another team's fixtures looking completely normal. So seed two teams with data in
/// both and assert every public read returns only the team in scope — the miss is caught the day the query is written, not the day
/// someone notices their season table has a stranger in it.
public class TeamDataScopingTests : ServiceTestBase
{
    private sealed record Fixture(int TeamId, int ClubId, int SeasonId, int GameId, int TrainingId, string PlayerName);

    [Fact]
    public async Task Every_season_scoped_read_returns_only_the_team_in_scope()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        Assert.Equal([a.SeasonId], (await Seasons.GetAllAsync()).Value!.Select(s => s.Id));
        Assert.Equal([a.GameId], (await Games.GetAllAsync()).Value!.Select(g => g.Id));
        Assert.Equal([a.TrainingId], (await Trainings.GetAllAsync()).Value!.Select(t => t.Id));
        Assert.Single((await Squads.GetSquadAsync(a.SeasonId)).Value!.Members);
        Assert.Empty((await Squads.GetSquadAsync(b.SeasonId)).Value!.Members);

        Scope(b);
        Assert.Equal([b.SeasonId], (await Seasons.GetAllAsync()).Value!.Select(s => s.Id));
        Assert.Equal([b.GameId], (await Games.GetAllAsync()).Value!.Select(g => g.Id));
        Assert.Equal([b.TrainingId], (await Trainings.GetAllAsync()).Value!.Select(t => t.Id));
        Assert.Single((await Squads.GetSquadAsync(b.SeasonId)).Value!.Members);
        Assert.Empty((await Squads.GetSquadAsync(a.SeasonId)).Value!.Members);
    }

    [Fact]
    public async Task A_game_read_by_id_belonging_to_another_team_is_not_found()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        Assert.True((await Games.GetByIdAsync(a.GameId)).IsSuccess);
        Assert.True((await Games.GetByIdAsync(b.GameId)).IsFailure);
        Assert.True((await Live.GetLiveAsync(b.GameId)).IsFailure);
    }

    [Fact]
    public async Task Todays_match_is_the_scoped_teams_fixture_not_another_teams()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        Assert.Equal(a.GameId, (await Live.GetTodaysMatchAsync()).Value!.Id);

        Scope(b);
        Assert.Equal(b.GameId, (await Live.GetTodaysMatchAsync()).Value!.Id);
    }

    [Fact]
    public async Task Preferences_for_another_teams_season_are_not_served()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        Assert.True((await Preferences.GetAsync(a.SeasonId)).IsSuccess);
        Assert.True((await Preferences.GetAsync(b.SeasonId)).IsFailure);
    }

    [Fact]
    public async Task The_statistics_count_only_the_scoped_teams_games()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        var forA = (await Stats.GetSeasonAsync(null)).Value!;
        Assert.Equal([a.PlayerName], forA.Stats.Players.Select(p => p.Player.DisplayName));

        Scope(b);
        var forB = (await Stats.GetSeasonAsync(null)).Value!;
        Assert.Equal([b.PlayerName], forB.Stats.Players.Select(p => p.Player.DisplayName));
    }

    /// The club-pool decision: two teams of one club draw from the same players, and a team in another club does not. A move between the
    /// club's teams keeps one history precisely because the player is not team-scoped.
    [Fact]
    public async Task Players_are_scoped_to_the_club_the_team_belongs_to()
    {
        var first = SeedTeam("Sole Club", "MO15-2");
        var shared = (await SeedPlayersAsync(2)).Select(p => p.DisplayName).ToHashSet();

        var second = SeedTeam("Sole Club", "MO17-1");
        var otherClub = SeedTeam("Other Club", "MO15-2");
        var outsider = (await Players.CreateAsync(new Player { FirstName = "Outsider", ClubId = otherClub.ClubId })).Value!;

        CurrentTeam.Id = first.Id;
        CurrentTeam.ClubId = first.ClubId;
        Assert.Equal(shared, (await Players.GetAllAsync()).Value!.Select(p => p.DisplayName).ToHashSet());

        CurrentTeam.Id = second.Id;
        CurrentTeam.ClubId = second.ClubId;
        Assert.Equal(shared, (await Players.GetAllAsync()).Value!.Select(p => p.DisplayName).ToHashSet());

        CurrentTeam.Id = otherClub.Id;
        CurrentTeam.ClubId = otherClub.ClubId;
        Assert.Equal([outsider.DisplayName], (await Players.GetAllAsync()).Value!.Select(p => p.DisplayName));
    }

    [Fact]
    public async Task A_write_against_another_teams_game_is_refused_and_leaves_it_untouched()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();

        Scope(a);
        Assert.True((await Games.DeleteAsync(b.GameId)).IsFailure);
        Assert.True((await Games.UpdateAsync(new Game { Id = b.GameId, Opponent = "Hijacked", Date = Now.Date })).IsFailure);
        Assert.True((await Games.AddCommentAsync(new GameComment { GameId = b.GameId, Body = "Hijacked" })).IsFailure);
        Assert.True((await Games.AddGoalAsync(new GameGoal { GameId = b.GameId, IsOpponentGoal = true })).IsFailure);

        Scope(b);
        var game = (await Games.GetByIdAsync(b.GameId)).Value!;
        Assert.NotEqual("Hijacked", game.Opponent);
        Assert.Empty((await Games.GetCommentsAsync(b.GameId, includePrivate: true)).Value!);
        Assert.Empty(game.Goals);
    }

    [Fact]
    public async Task A_player_write_against_another_club_is_refused()
    {
        var (a, b) = await ArrangeTwoTeamsAsync();
        var bPlayerId = (await Read().Players.IgnoreQueryFilters().Where(p => p.ClubId == b.ClubId).Select(p => p.Id).FirstAsync());

        Scope(a);
        Assert.True((await Players.DeleteAsync(bPlayerId)).IsFailure);
        Assert.True((await Players.UpdateAsync(new Player { Id = bPlayerId, FirstName = "Hijacked", ClubId = a.ClubId })).IsFailure);

        Scope(b);
        Assert.Equal(b.PlayerName, (await Players.GetByIdAsync(bPlayerId)).Value!.DisplayName);
    }

    private void Scope(Fixture team)
    {
        CurrentTeam.Id = team.TeamId;
        CurrentTeam.ClubId = team.ClubId;
    }

    /// Two teams in two clubs, each with a season, a game today, a training, a squad member and a preferences row — enough that a read
    /// that forgot its team would come back with the wrong count rather than empty.
    private async Task<(Fixture A, Fixture B)> ArrangeTwoTeamsAsync() =>
        (await SeedTeamWithDataAsync("Club A", "MO15-2", "Ann"),
         await SeedTeamWithDataAsync("Club B", "MO17-1", "Bea"));

    private async Task<Fixture> SeedTeamWithDataAsync(string clubName, string teamName, string playerName)
    {
        var team = SeedTeam(clubName, teamName);
        var season = await SeedSeasonAsync();

        var player = (await Players.CreateAsync(new Player { FirstName = playerName })).Value!;
        await Squads.AddMemberAsync(season.Id, player.Id);

        var game = (await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, date: Now.Date))).Value!;
        var training = (await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.Date.AddDays(-1) })).Value!;
        await Preferences.GetAsync(season.Id);

        return new Fixture(team.Id, team.ClubId, season.Id, game.Id, training.Id, player.DisplayName);
    }
}
