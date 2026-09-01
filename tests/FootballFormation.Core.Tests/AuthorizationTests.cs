namespace FootballFormation.Core.Tests;

/// Nobody reaches these methods unauthorized today, which is exactly why the tests exist: this guard is the layer that keeps holding
/// when the markup stops, and a silent regression in it would look like nothing at all.
public class AuthorizationTests : ServiceTestBase
{
    private const string NotThisTeam = "You can only manage accounts for your own team";

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
    public async Task Trainings_are_the_one_read_that_is_not_public()
    {
        var season = await SeedSeasonAsync();
        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now, Notes = "Ill, back next week" });

        CurrentUser.IsAdmin = false;

        // The absence and the note beside it are personal, so this read is guarded at the service boundary as well as by the page's
        // [Authorize] — the markup gate stops holding the moment the service is reached another way.
        var result = await Trainings.GetAllAsync(season.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);

        // And by way of the attendance figures built on top of it, which carry no guard of their own and must not gain a cache in
        // front of the one that has it.
        var attendance = await Stats.GetTrainingAttendanceAsync(season.Id);

        Assert.True(attendance.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, attendance.ErrorKey);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_a_training()
    {
        var season = await SeedSeasonAsync();

        CurrentUser.IsAdmin = false;

        var result = await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now });

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().Trainings);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_rewrite_who_was_at_a_training()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var training = (await Trainings.CreateAsync(
            new Training { SeasonId = season.Id, Date = Now, UnavailablePlayerIds = [players[0].Id] })).Value!;

        CurrentUser.IsAdmin = false;

        training.UnavailablePlayerIds = [];
        var result = await Trainings.UpdateAsync(training);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Equal([players[0].Id], Read().Trainings.Single().UnavailablePlayerIds);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_delete_a_training()
    {
        var season = await SeedSeasonAsync();
        var training = (await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now })).Value!;

        CurrentUser.IsAdmin = false;

        var result = await Trainings.DeleteAsync(training.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Single(Read().Trainings);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_write_a_season_full_of_trainings_through_the_preferences()
    {
        var season = await SeedSeasonAsync();
        var prefs = (await Preferences.GetAsync(season.Id)).Value!;
        prefs.TrainingDays = [DayOfWeek.Tuesday];
        prefs.FirstTrainingDate = season.StartDate.Date;
        prefs.LastTrainingDate = season.EndDate.Date;

        CurrentUser.IsAdmin = false;

        // Saving preferences now writes the sessions the period implies, so the guard on it is holding back a table's worth of rows
        // rather than one settings row.
        var result = await Preferences.SaveAsync(prefs);

        Assert.True(result.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, result.ErrorKey);
        Assert.Empty(Read().Trainings);
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
    public async Task The_clubs_and_teams_are_a_public_read()
    {
        await TeamsAndClubs.EnsureSeededAsync("GJS", "MO15-2");

        CurrentUser.IsAdmin = false;
        CurrentUser.IsApplicationAdmin = false;

        Assert.True((await TeamsAndClubs.GetClubsAsync()).IsSuccess);
        Assert.True((await TeamsAndClubs.GetTeamsAsync()).IsSuccess);
        Assert.True((await TeamsAndClubs.GetCurrentAsync()).IsSuccess);
    }

    [Fact]
    public async Task An_admin_who_is_not_an_application_admin_cannot_add_a_team()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;

        // The rung this role exists for: running a team is not the same authority as deciding which teams the app serves.
        CurrentUser.IsApplicationAdmin = false;

        var team = await TeamsAndClubs.CreateTeamAsync(new Team { ClubId = club.Id, Name = "MO15-2" });

        Assert.True(team.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, team.ErrorKey);
        Assert.Empty(Read().Teams);
    }

    [Fact]
    public async Task An_admin_who_is_not_an_application_admin_cannot_add_or_delete_a_club()
    {
        var club = (await TeamsAndClubs.CreateClubAsync(new Club { Name = "GJS" })).Value!;

        CurrentUser.IsApplicationAdmin = false;

        var created = await TeamsAndClubs.CreateClubAsync(new Club { Name = "SV Zwaluwen" });
        var deleted = await TeamsAndClubs.DeleteClubAsync(club.Id);

        Assert.Equal(ServiceOperation.NotAllowedKey, created.ErrorKey);
        Assert.Equal(ServiceOperation.NotAllowedKey, deleted.ErrorKey);
        Assert.Single(Read().Clubs);
    }

    [Fact]
    public async Task An_admin_cannot_touch_an_account_on_another_team()
    {
        var mine = SeedTeam("GJS", "MO15-2");
        var theirs = SeedTeam("SV Zwaluwen", "MO15-1");

        var intruder = (await Users.CreateAsync("Coach", "coach", "long-enough-password", UserRole.Admin, theirs.Id)).Value!;

        // An admin of one team passes RunAdminAsync for their own, and /users would otherwise be the way from there to every other
        // team's accounts — reset a password, keep the account.
        CurrentUser.IsApplicationAdmin = false;
        CurrentUser.AdminTeamId = mine.Id;
        CurrentTeam.Id = mine.Id;

        var renamed = await Users.UpdateAsync(intruder.Id, "Taken over", "coach", UserRole.Admin, mine.Id);
        var reset = await Users.SetPasswordAsync(intruder.Id, "another-long-password");
        var deleted = await Users.DeleteAsync(intruder.Id);

        Assert.Equal(NotThisTeam, renamed.ErrorKey);
        Assert.Equal(NotThisTeam, reset.ErrorKey);
        Assert.Equal(NotThisTeam, deleted.ErrorKey);
        Assert.Equal("Coach", Read().Users.Single(u => u.Id == intruder.Id).DisplayName);
    }

    [Fact]
    public async Task An_admin_cannot_hand_an_account_to_another_team()
    {
        var mine = SeedTeam("GJS", "MO15-2");
        var theirs = SeedTeam("SV Zwaluwen", "MO15-1");

        var user = (await Users.CreateAsync("Coach", "coach", "long-enough-password", UserRole.Admin, mine.Id)).Value!;

        CurrentUser.IsApplicationAdmin = false;
        CurrentUser.AdminTeamId = mine.Id;
        CurrentTeam.Id = mine.Id;

        // The other direction of the same authority: giving an account away is granting a team you do not run.
        var moved = await Users.UpdateAsync(user.Id, "Coach", "coach", UserRole.Admin, theirs.Id);

        Assert.Equal(NotThisTeam, moved.ErrorKey);
        Assert.Equal(mine.Id, Read().Users.Single(u => u.Id == user.Id).TeamId);
    }

    [Fact]
    public async Task An_application_admin_manages_every_team()
    {
        SeedTeam("GJS", "MO15-2");
        var theirs = SeedTeam("SV Zwaluwen", "MO15-1");

        var user = (await Users.CreateAsync("Coach", "coach", "long-enough-password", UserRole.Admin, theirs.Id)).Value!;

        Assert.True((await Users.UpdateAsync(user.Id, "Renamed", "coach", UserRole.Admin, theirs.Id)).IsSuccess);
        Assert.True((await Users.SetPasswordAsync(user.Id, "another-long-password")).IsSuccess);
    }

    [Fact]
    public async Task An_admin_sees_only_the_accounts_on_the_team_in_scope()
    {
        var mine = SeedTeam("GJS", "MO15-2");
        var theirs = SeedTeam("SV Zwaluwen", "MO15-1");

        await Users.CreateAsync("Mine", "mine", "long-enough-password", UserRole.Admin, mine.Id);
        await Users.CreateAsync("Theirs", "theirs", "long-enough-password", UserRole.Admin, theirs.Id);

        CurrentUser.IsApplicationAdmin = false;
        CurrentUser.AdminTeamId = mine.Id;
        CurrentTeam.Id = mine.Id;

        var users = await Users.GetAllAsync();

        Assert.Equal("Mine", Assert.Single(users.Value!).DisplayName);
    }

    [Fact]
    public async Task Pointing_the_team_cookie_at_another_team_does_not_list_its_accounts()
    {
        var mine = SeedTeam("GJS", "MO15-2");
        var theirs = SeedTeam("SV Zwaluwen", "MO15-1");

        await Users.CreateAsync("Theirs", "theirs", "long-enough-password", UserRole.Admin, theirs.Id);

        // The cookie is a view choice anyone can make, so it must not be what decides who sees a list of names and logins — the read
        // asks the same question the writes do.
        CurrentUser.IsApplicationAdmin = false;
        CurrentUser.AdminTeamId = mine.Id;
        CurrentTeam.Id = theirs.Id;

        var users = await Users.GetAllAsync();

        Assert.True(users.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, users.ErrorKey);
    }

    [Fact]
    public async Task The_account_list_is_not_one_of_the_public_reads()
    {
        SeedTeam();

        CurrentUser.IsAdmin = false;
        CurrentUser.IsApplicationAdmin = false;

        // Unlike the squad, the fixtures and the statistics: this one is names and logins, so it is guarded at the service boundary
        // rather than only by the page's [Authorize].
        var users = await Users.GetAllAsync();

        Assert.True(users.IsFailure);
        Assert.Equal(ServiceOperation.NotAllowedKey, users.ErrorKey);
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
