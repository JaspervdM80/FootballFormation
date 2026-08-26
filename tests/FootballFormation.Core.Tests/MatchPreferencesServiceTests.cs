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

    private async Task SetTrainingDaysAsync(int seasonId, params DayOfWeek[] days)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.TrainingDays = [.. days];
        await Preferences.SaveAsync(prefs);
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
