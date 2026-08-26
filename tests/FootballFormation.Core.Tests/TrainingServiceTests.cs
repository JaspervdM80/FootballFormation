namespace FootballFormation.Core.Tests;

/// A training session is the second thing in the app with a date and a list of people who were not there, and it has to behave like the
/// first: filed under the season its date falls in, ordered in memory rather than by SQLite's text dates, and readable back exactly as it
/// was written.
public class TrainingServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_session_is_filed_under_the_season_its_date_falls_in()
    {
        var season = await SeedSeasonAsync();

        // SeasonId 0 is the dialog's "resolve from the date" — the season is never the caller's to state.
        var result = await Trainings.CreateAsync(new Training { Date = Now });

        Assert.True(result.IsSuccess);
        Assert.Equal(season.Id, Read().Trainings.Single().SeasonId);
    }

    [Fact]
    public async Task A_session_dated_into_a_season_that_does_not_exist_yet_creates_it()
    {
        await SeedSeasonAsync();

        // Two years on is past every season on file. The windows are gapless, so the date still names exactly one — it just has to be
        // created first, the same way a fixture entered for next year does.
        var future = Now.AddYears(2);
        var result = await Trainings.CreateAsync(new Training { Date = future });

        Assert.True(result.IsSuccess);
        var season = Read().Seasons.ToList().Single(s => s.Contains(future));
        Assert.Equal(season.Id, result.Value!.SeasonId);
    }

    [Fact]
    public async Task Who_was_missing_and_the_note_survive_the_round_trip()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(3);

        await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now,
            UnavailablePlayerIds = [players[0].Id, players[2].Id],
            Notes = "Passing under pressure. Two out ill.",
        });

        // Read back through a fresh context: the ids are a CSV text column behind a value converter, and a missing ValueComparer would
        // show up here as an empty list rather than as a failure.
        var stored = Read().Trainings.Single();
        Assert.Equal([players[0].Id, players[2].Id], stored.UnavailablePlayerIds);
        Assert.Equal("Passing under pressure. Two out ill.", stored.Notes);
    }

    [Fact]
    public async Task A_session_with_nobody_missing_and_nothing_to_say_is_ordinary()
    {
        var season = await SeedSeasonAsync();

        var result = await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now });

        Assert.True(result.IsSuccess);
        var stored = Read().Trainings.Single();
        Assert.Empty(stored.UnavailablePlayerIds);
        Assert.Null(stored.Notes);
    }

    [Fact]
    public async Task The_list_comes_back_newest_first_with_two_sessions_in_a_day_in_entry_order()
    {
        var season = await SeedSeasonAsync();

        var lastWeek = (await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.AddDays(-7) })).Value!;
        var earlyToday = (await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.Date })).Value!;
        var lateToday = (await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now.Date })).Value!;

        var result = await Trainings.GetAllAsync(season.Id);

        // The two same-day rows carry the identical date, so only the id tie-break keeps them in the order they were entered.
        Assert.Equal([earlyToday.Id, lateToday.Id, lastWeek.Id], result.Value!.Select(t => t.Id));
    }

    [Fact]
    public async Task The_list_is_scoped_to_the_season_asked_for()
    {
        var thisSeason = await SeedSeasonAsync();
        var nextSeason = await SeedSeasonAsync(Now.AddYears(1), isCurrent: false);

        await Trainings.CreateAsync(new Training { SeasonId = thisSeason.Id, Date = Now });
        await Trainings.CreateAsync(new Training { SeasonId = nextSeason.Id, Date = Now.AddYears(1) });

        Assert.Single((await Trainings.GetAllAsync(thisSeason.Id)).Value!);
        Assert.Equal(2, (await Trainings.GetAllAsync()).Value!.Count);
    }

    [Fact]
    public async Task Moving_a_session_leaves_the_absences_it_already_recorded_alone()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var training = (await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now,
            UnavailablePlayerIds = [players[1].Id],
        })).Value!;

        training.Date = Now.AddDays(1);
        Assert.True((await Trainings.UpdateAsync(training)).IsSuccess);

        var stored = Read().Trainings.Single();
        Assert.Equal(Now.AddDays(1), stored.Date);
        Assert.Equal([players[1].Id], stored.UnavailablePlayerIds);
    }

    [Fact]
    public async Task Deleting_a_session_takes_nothing_but_itself()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        await Squads.AddMemberAsync(season.Id, players[0].Id);
        var training = (await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now,
            UnavailablePlayerIds = [players[0].Id],
        })).Value!;

        Assert.True((await Trainings.DeleteAsync(training.Id)).IsSuccess);

        // The ids are a text column, not foreign keys, so nobody leaves the squad or the club with the session they missed.
        Assert.Empty(Read().Trainings);
        Assert.Equal(2, Read().Players.Count());
        Assert.Single(Read().SeasonSquadMembers);
    }

    [Fact]
    public async Task A_session_that_did_not_take_place_records_nobody_as_absent()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);

        // The caller may pass absences anyway — the dialog hides the picker, but the markup is not what holds the rule up.
        await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now,
            DidNotTakePlace = true,
            UnavailablePlayerIds = [players[0].Id],
            Notes = "Vorst, veld dicht",
        });

        var stored = Read().Trainings.Single();
        Assert.True(stored.DidNotTakePlace);
        Assert.Empty(stored.UnavailablePlayerIds);
        Assert.Equal("Vorst, veld dicht", stored.Notes);
    }

    [Fact]
    public async Task Marking_a_session_that_was_held_as_cancelled_drops_the_absences_it_had()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(2);
        var training = (await Trainings.CreateAsync(new Training
        {
            SeasonId = season.Id,
            Date = Now,
            UnavailablePlayerIds = [players[0].Id, players[1].Id],
        })).Value!;

        training.DidNotTakePlace = true;
        Assert.True((await Trainings.UpdateAsync(training)).IsSuccess);

        // The update path is the one that actually loses data: a session entered as held, then corrected, would otherwise keep saying
        // two people missed an evening nobody had.
        var stored = Read().Trainings.Single();
        Assert.True(stored.DidNotTakePlace);
        Assert.Empty(stored.UnavailablePlayerIds);
    }

    [Fact]
    public async Task A_cancelled_session_can_be_put_back_as_one_that_was_held()
    {
        var season = await SeedSeasonAsync();
        var players = await SeedPlayersAsync(1);
        var training = (await Trainings.CreateAsync(
            new Training { SeasonId = season.Id, Date = Now, DidNotTakePlace = true })).Value!;

        training.DidNotTakePlace = false;
        training.UnavailablePlayerIds = [players[0].Id];
        Assert.True((await Trainings.UpdateAsync(training)).IsSuccess);

        var stored = Read().Trainings.Single();
        Assert.False(stored.DidNotTakePlace);
        Assert.Equal([players[0].Id], stored.UnavailablePlayerIds);
    }

    [Fact]
    public async Task Deleting_a_session_that_is_not_there_is_refused_rather_than_ignored()
    {
        Assert.True((await Trainings.DeleteAsync(9999)).IsFailure);
    }

}
