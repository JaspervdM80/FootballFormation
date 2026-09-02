namespace FootballFormation.Core.Tests;

/// The list reads forwards — this week, then the weeks ahead, with the ones already over below them. Whole weeks on both counts, which is
/// what keeps a Tuesday session from dropping to the bottom of the page on the Wednesday.
public class TrainingTests
{
    /// Saturday, the same day ServiceTestBase runs at. Its ISO week opens on Monday 9 March.
    private static readonly DateTime Today = new(2026, 3, 14);

    [Fact]
    public void This_week_reads_first_and_the_weeks_already_over_read_last()
    {
        List<Training> trainings =
        [
            new() { Id = 1, Date = new DateTime(2026, 3, 3) },
            new() { Id = 2, Date = new DateTime(2026, 3, 17) },
            new() { Id = 3, Date = new DateTime(2026, 3, 12) },
        ];

        Assert.Equal([3, 2, 1], trainings.UpcomingFirst(Today).Select(t => t.Id));
    }

    [Fact]
    public void A_session_earlier_this_week_stays_above_the_line()
    {
        List<Training> trainings =
        [
            new() { Id = 1, Date = new DateTime(2026, 3, 6) },
            new() { Id = 2, Date = new DateTime(2026, 3, 10) },
        ];

        // Tuesday is behind us and Friday last week is not much further, but the week they fall in is what decides — this one is still
        // being played out.
        Assert.Equal([2, 1], trainings.UpcomingFirst(Today).Select(t => t.Id));
    }

    [Fact]
    public void The_weeks_already_over_read_most_recent_first()
    {
        List<Training> trainings =
        [
            new() { Id = 1, Date = new DateTime(2026, 2, 24) },
            new() { Id = 2, Date = new DateTime(2026, 3, 3) },
        ];

        // Downwards from the line: last week, then the week before it. The evenings the coach still has to write up are the near ones.
        Assert.Equal([2, 1], trainings.UpcomingFirst(Today).Select(t => t.Id));
    }

    [Fact]
    public void Two_sessions_on_one_day_keep_the_order_they_were_entered_in()
    {
        var day = new DateTime(2026, 3, 17);
        List<Training> trainings =
        [
            new() { Id = 2, Date = day },
            new() { Id = 1, Date = day },
            new() { Id = 3, Date = day.AddDays(1) },
        ];

        // Without the tie-break the two same-day rows would come back in whatever order the sort happened to leave them.
        Assert.Equal([1, 2, 3], trainings.UpcomingFirst(Today).Select(t => t.Id));
    }

    [Fact]
    public void Only_an_evening_that_is_behind_us_and_went_ahead_counts_as_held()
    {
        Assert.True(new Training { Date = Today.AddDays(-1) }.HasBeenHeld(Today));

        // Today's evening is still to come as far as the register is concerned: its absences can be entered up to the whistle, so a
        // badge or a percentage read off it now would move again this evening.
        Assert.False(new Training { Date = Today }.HasBeenHeld(Today));
        Assert.False(new Training { Date = Today.AddDays(1) }.HasBeenHeld(Today));
        Assert.False(new Training { Date = Today.AddDays(-1), DidNotTakePlace = true }.HasBeenHeld(Today));
    }

    [Fact]
    public void A_generated_session_with_nothing_recorded_against_it_is_the_only_one_the_schedule_may_remove()
    {
        var scheduled = new Training { FromSchedule = true };

        Assert.True(scheduled.IsUnusedSchedule);
        Assert.False(new Training().IsUnusedSchedule);
        Assert.False(new Training { FromSchedule = true, Notes = "Partijvorm" }.IsUnusedSchedule);
        Assert.False(new Training { FromSchedule = true, UnavailablePlayerIds = [7] }.IsUnusedSchedule);
        Assert.False(new Training { FromSchedule = true, DidNotTakePlace = true }.IsUnusedSchedule);
    }
}
