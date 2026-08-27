namespace FootballFormation.Core.Tests;

/// "Next match date" fails quietly — every branch returns a plausible date, so a wrong one shows up as a dialog opening on an odd day
/// rather than as an error. The clock is <see cref="ServiceTestBase.Time"/>, so "today" is a fact the test states.
public class MatchPreferencesServiceTests : ServiceTestBase
{
    // Now is Saturday 14 March 2026 — the season window runs Jul 2025 – Jun 2026.
    private static readonly DateTime Saturday = new(2026, 3, 14);

    [Fact]
    public async Task A_season_gets_its_preferences_on_first_read()
    {
        var season = await SeedSeasonAsync();

        var prefs = await Preferences.GetAsync(season.Id);

        Assert.True(prefs.IsSuccess);
        Assert.Equal(season.Id, prefs.Value!.SeasonId);
        Assert.Single(Read().MatchPreferences);
    }

    [Fact]
    public async Task Reading_twice_does_not_create_a_second_row()
    {
        var season = await SeedSeasonAsync();

        await Preferences.GetAsync(season.Id);
        await Preferences.GetAsync(season.Id);

        Assert.Single(Read().MatchPreferences);
    }

    [Fact]
    public async Task Without_a_season_there_is_nothing_to_read()
    {
        var prefs = await Preferences.GetAsync(0);

        Assert.True(prefs.IsFailure);
        Assert.Empty(Read().MatchPreferences);
    }

    [Fact]
    public async Task A_new_season_inherits_the_previous_seasons_settings()
    {
        var last = await SeedSeasonAsync(covering: Saturday.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Saturday);

        var lastPrefs = (await Preferences.GetAsync(last.Id)).Value!;
        lastPrefs.GameDurationMinutes = 50;
        lastPrefs.DefaultFormation = FormationType.F442;
        lastPrefs.MatchDay = DayOfWeek.Sunday;
        await Preferences.SaveAsync(lastPrefs);

        var inherited = (await Preferences.GetAsync(next.Id)).Value!;

        // The point of inheriting: a hardcoded default here would silently reset game length every July.
        Assert.Equal(50, inherited.GameDurationMinutes);
        Assert.Equal(FormationType.F442, inherited.DefaultFormation);
        Assert.Equal(DayOfWeek.Sunday, inherited.MatchDay);
        Assert.Equal(next.Id, inherited.SeasonId);
    }

    [Fact]
    public async Task An_earlier_season_is_preferred_over_a_later_one()
    {
        var earlier = await SeedSeasonAsync(covering: Saturday.AddYears(-1), isCurrent: false);
        var later = await SeedSeasonAsync(covering: Saturday.AddYears(1), isCurrent: false);
        var current = await SeedSeasonAsync(covering: Saturday);

        await SetDurationAsync(earlier.Id, 50);
        await SetDurationAsync(later.Id, 90);

        var inherited = (await Preferences.GetAsync(current.Id)).Value!;

        // Both directions have a row, so the rule decides: inherit from the season before, not one that has not happened yet.
        Assert.Equal(50, inherited.GameDurationMinutes);
    }

    [Fact]
    public async Task With_nothing_earlier_any_existing_row_is_better_than_a_blank_one()
    {
        var later = await SeedSeasonAsync(covering: Saturday.AddYears(1), isCurrent: false);
        var current = await SeedSeasonAsync(covering: Saturday);

        await SetDurationAsync(later.Id, 90);

        var inherited = (await Preferences.GetAsync(current.Id)).Value!;

        // With no history to copy, settings someone actually chose beat the hardcoded defaults even if chosen for a later season.
        Assert.Equal(90, inherited.GameDurationMinutes);
    }

    [Fact]
    public async Task With_no_games_the_next_match_is_the_coming_match_day()
    {
        var season = await SeedSeasonAsync();
        await SetMatchDayAsync(season.Id, DayOfWeek.Wednesday);

        var next = await Preferences.GetNextMatchDateAsync(season.Id);

        // Today is Saturday 14 March; the next Wednesday is the 18th.
        Assert.Equal(new DateTime(2026, 3, 18), next.Value);
    }

    [Fact]
    public async Task Today_counts_when_today_is_the_match_day()
    {
        var season = await SeedSeasonAsync();
        await SetMatchDayAsync(season.Id, DayOfWeek.Saturday);

        var next = await Preferences.GetNextMatchDateAsync(season.Id);

        // Measuring from today, today is a valid answer — a game can still be entered on match day.
        Assert.Equal(Saturday, next.Value);
    }

    [Fact]
    public async Task A_fixture_already_scheduled_ahead_pushes_the_next_one_past_it()
    {
        var season = await SeedSeasonAsync();
        await SetMatchDayAsync(season.Id, DayOfWeek.Saturday);
        await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, date: Saturday.AddDays(7)));

        var next = await Preferences.GetNextMatchDateAsync(season.Id);

        // Entering a run of fixtures steps forward one match day at a time, never landing twice on the same date.
        Assert.Equal(Saturday.AddDays(14), next.Value);
    }

    [Fact]
    public async Task A_last_game_in_the_past_is_measured_from_today_instead()
    {
        var season = await SeedSeasonAsync();
        await SetMatchDayAsync(season.Id, DayOfWeek.Saturday);
        await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id, date: Saturday.AddDays(-28)));

        var next = await Preferences.GetNextMatchDateAsync(season.Id);

        // Stepping off a game that is already behind us would open the dialog months in the past.
        Assert.Equal(Saturday, next.Value);
    }

    [Fact]
    public async Task Games_from_another_season_do_not_move_this_seasons_date()
    {
        var other = await SeedSeasonAsync(covering: Saturday.AddYears(-1), isCurrent: false);
        var season = await SeedSeasonAsync(covering: Saturday);
        await SetMatchDayAsync(season.Id, DayOfWeek.Saturday);
        await Games.CreateAsync(TestData.Game(id: 0, seasonId: other.Id, date: Saturday.AddYears(-1)));

        var next = await Preferences.GetNextMatchDateAsync(season.Id);

        Assert.Equal(Saturday, next.Value);
    }

    [Fact]
    public async Task A_future_season_is_measured_from_its_own_opening_day()
    {
        var future = await SeedSeasonAsync(covering: Saturday.AddYears(1), isCurrent: false);
        await SetMatchDayAsync(future.Id, DayOfWeek.Saturday);

        var next = (await Preferences.GetNextMatchDateAsync(future.Id)).Value;

        // "Today" is meaningless for a season that has not started, so this must not propose a date from the one we are living in.
        Assert.InRange(next, future.StartDate.Date, future.EndDate.Date);
        Assert.Equal(DayOfWeek.Saturday, next.DayOfWeek);
    }

    [Fact]
    public async Task A_season_that_is_already_over_stays_inside_its_own_window()
    {
        var past = await SeedSeasonAsync(covering: Saturday.AddYears(-2), isCurrent: false);
        await SetMatchDayAsync(past.Id, DayOfWeek.Saturday);

        var next = (await Preferences.GetNextMatchDateAsync(past.Id)).Value;

        // Entering a result late must not propose a date that belongs to the next season.
        Assert.InRange(next, past.StartDate.Date, past.EndDate.Date);
        Assert.Equal(DayOfWeek.Saturday, next.DayOfWeek);
    }

    [Fact]
    public async Task The_training_days_carry_into_the_next_season_the_way_the_match_day_does()
    {
        var last = await SeedSeasonAsync(covering: Saturday.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Saturday);

        await SetTrainingDaysAsync(last.Id, DayOfWeek.Tuesday, DayOfWeek.Thursday);

        var inherited = (await Preferences.GetAsync(next.Id)).Value!;

        // A copy, not the same list: editing next season's days must not reach back into last season's row.
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday], inherited.TrainingDays);
        Assert.NotSame((await Preferences.GetAsync(last.Id)).Value!.TrainingDays, inherited.TrainingDays);
    }

    [Fact]
    public async Task The_next_training_date_lands_on_the_soonest_of_the_days_chosen()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        await SetTrainingDaysAsync(season.Id, DayOfWeek.Tuesday, DayOfWeek.Thursday);

        var next = (await Preferences.GetNextTrainingDateAsync(season.Id)).Value;

        // Today is Saturday, so Tuesday is the nearer of the two even though Thursday is listed second.
        Assert.Equal(new DateTime(2026, 3, 17), next);
    }

    [Fact]
    public async Task A_session_already_entered_pushes_the_next_one_past_it()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        await SetTrainingDaysAsync(season.Id, DayOfWeek.Tuesday, DayOfWeek.Thursday);
        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = new DateTime(2026, 3, 17) });

        var next = (await Preferences.GetNextTrainingDateAsync(season.Id)).Value;

        // Two sessions on one day is legal, but proposing the day already filled in is never what was meant.
        Assert.Equal(new DateTime(2026, 3, 19), next);
    }

    [Fact]
    public async Task With_no_training_days_chosen_the_date_falls_back_to_today()
    {
        var season = await SeedSeasonAsync(covering: Saturday);

        // The honest answer while the setting is empty: there is no weekday to land on, and refusing would leave the dialog with no date
        // at all.
        Assert.Equal(Saturday, (await Preferences.GetNextTrainingDateAsync(season.Id)).Value);
    }

    [Fact]
    public async Task Without_a_season_there_is_no_training_date_to_propose()
    {
        // "All seasons" is a real choice in the picker, and the dialog opens under it — so this has to answer rather than throw.
        Assert.True((await Preferences.GetNextTrainingDateAsync(0)).IsFailure);
    }

    [Fact]
    public async Task A_season_already_over_proposes_a_training_date_inside_its_own_window()
    {
        var past = await SeedSeasonAsync(covering: Saturday.AddYears(-2), isCurrent: false);
        await SetTrainingDaysAsync(past.Id, DayOfWeek.Tuesday);

        var next = (await Preferences.GetNextTrainingDateAsync(past.Id)).Value;

        Assert.InRange(next, past.StartDate.Date, past.EndDate.Date);
        Assert.Equal(DayOfWeek.Tuesday, next.DayOfWeek);
    }

    [Fact]
    public async Task A_one_off_session_before_the_period_does_not_drag_the_next_date_out_with_it()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        await SetTrainingDaysAsync(season.Id, DayOfWeek.Tuesday);
        await SetTrainingPeriodAsync(season.Id, new DateTime(2026, 4, 1), new DateTime(2026, 5, 31));

        // An extra evening outside the period is allowed on purpose, and it is still ahead of us — so it becomes the reference the next
        // date steps off. Without a floor the answer follows it out of the period, which is the case the period exists to prevent.
        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = new DateTime(2026, 3, 20) });

        Assert.Equal(new DateTime(2026, 4, 7), (await Preferences.GetNextTrainingDateAsync(season.Id)).Value);
    }

    [Fact]
    public async Task The_next_training_date_starts_at_the_first_training_rather_than_at_the_season()
    {
        // A season we are not in yet, so "today" cannot be the answer and the window's opening day is what gets measured from.
        var next = await SeedSeasonAsync(covering: Saturday.AddYears(1), isCurrent: false);
        await SetTrainingDaysAsync(next.Id, DayOfWeek.Tuesday);
        await SetTrainingPeriodAsync(next.Id, new DateTime(2026, 8, 17), null);

        // Without the period this would propose a Tuesday in early July, the season's own opening month, to a team that trains from
        // mid-August.
        Assert.Equal(new DateTime(2026, 8, 18), (await Preferences.GetNextTrainingDateAsync(next.Id)).Value);
    }

    [Fact]
    public async Task The_next_training_date_never_runs_past_the_last_training()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        await SetTrainingDaysAsync(season.Id, DayOfWeek.Tuesday);
        await SetTrainingPeriodAsync(season.Id, null, new DateTime(2026, 3, 10));

        // Today is Saturday 14 March, past the end of the period — so the answer walks back to the last training day inside it rather
        // than proposing a date the team is no longer training on.
        Assert.Equal(new DateTime(2026, 3, 10), (await Preferences.GetNextTrainingDateAsync(season.Id)).Value);
    }

    [Fact]
    public async Task An_unset_training_period_still_measures_from_the_season_itself()
    {
        var next = await SeedSeasonAsync(covering: Saturday.AddYears(1), isCurrent: false);
        await SetTrainingDaysAsync(next.Id, DayOfWeek.Tuesday);

        // The compatibility case: every row written before the period existed has both ends null, and must behave exactly as it did.
        var proposed = (await Preferences.GetNextTrainingDateAsync(next.Id)).Value;

        Assert.Equal(DayOfWeek.Tuesday, proposed.DayOfWeek);
        Assert.InRange(proposed, next.StartDate.Date, next.StartDate.Date.AddDays(7));
    }

    [Fact]
    public async Task The_training_period_is_not_carried_into_the_next_season()
    {
        var last = await SeedSeasonAsync(covering: Saturday.AddYears(-1), isCurrent: false);
        var next = await SeedSeasonAsync(covering: Saturday);

        await SetTrainingDaysAsync(last.Id, DayOfWeek.Tuesday);
        // Inside last season's own window (Jul 2024 – Jun 2025), or SaveAsync would refuse it before this test could ask its question.
        await SetTrainingPeriodAsync(last.Id, new DateTime(2024, 8, 20), new DateTime(2025, 5, 27));

        var inherited = (await Preferences.GetAsync(next.Id)).Value!;

        // The days carry, the dates do not: last August's opening night is not a guess at this one, and a date from the previous season
        // would fail SaveAsync's own window check the moment anyone pressed Save.
        Assert.Equal([DayOfWeek.Tuesday], inherited.TrainingDays);
        Assert.Null(inherited.FirstTrainingDate);
        Assert.Null(inherited.LastTrainingDate);
    }

    [Fact]
    public async Task Preferences_for_a_season_that_is_gone_are_refused_rather_than_saved()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        var prefs = (await Preferences.GetAsync(season.Id)).Value!;
        prefs.SeasonId = 9999;

        // What an admin editing preferences while somebody else deletes the season would hand in. The period check needs the season's
        // window, so there is nothing to validate against and a raw foreign-key violation is not an answer.
        var result = await Preferences.SaveAsync(prefs);

        Assert.True(result.IsFailure);
        Assert.Equal("Season not found", result.ErrorKey);
    }

    [Fact]
    public async Task A_training_period_that_ends_before_it_starts_is_refused()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        var prefs = (await Preferences.GetAsync(season.Id)).Value!;
        prefs.FirstTrainingDate = new DateTime(2026, 3, 10);
        prefs.LastTrainingDate = new DateTime(2026, 3, 3);

        var result = await Preferences.SaveAsync(prefs);

        Assert.True(result.IsFailure);
        Assert.Equal("The last training must not be before the first", result.ErrorKey);
    }

    [Fact]
    public async Task A_training_period_reaching_outside_its_season_is_refused()
    {
        var season = await SeedSeasonAsync(covering: Saturday);
        var prefs = (await Preferences.GetAsync(season.Id)).Value!;
        prefs.LastTrainingDate = season.EndDate.Date.AddDays(1);

        var result = await Preferences.SaveAsync(prefs);

        // A date past the window belongs to the next season, and a session dated there would be filed under it — so the period would be
        // describing a season it is not attached to.
        Assert.True(result.IsFailure);
        Assert.Equal("The training period must fall inside season {0}", result.ErrorKey);
        Assert.Null(Read().MatchPreferences.Single().LastTrainingDate);
    }

    private async Task SetTrainingDaysAsync(int seasonId, params DayOfWeek[] days)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.TrainingDays = [.. days];
        await Preferences.SaveAsync(prefs);
    }

    private async Task SetTrainingPeriodAsync(int seasonId, DateTime? first, DateTime? last)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.FirstTrainingDate = first;
        prefs.LastTrainingDate = last;
        Assert.True((await Preferences.SaveAsync(prefs)).IsSuccess);
    }

    private async Task SetMatchDayAsync(int seasonId, DayOfWeek matchDay)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.MatchDay = matchDay;
        await Preferences.SaveAsync(prefs);
    }

    private async Task SetDurationAsync(int seasonId, int minutes)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.GameDurationMinutes = minutes;
        await Preferences.SaveAsync(prefs);
    }
}
