namespace FootballFormation.Core.Tests;

/// The date arithmetic behind the generated sessions. Pure, so the cases that are awkward to reach through a service — an empty window,
/// an end before its start — are cheap to state here.
public class TrainingScheduleTests
{
    [Fact]
    public void Every_chosen_weekday_between_the_two_dates_gets_a_date()
    {
        var dates = TrainingSchedule.DatesIn(
            new DateTime(2026, 3, 2), new DateTime(2026, 3, 15), [DayOfWeek.Tuesday, DayOfWeek.Thursday]);

        Assert.Equal(
            [new(2026, 3, 3), new(2026, 3, 5), new(2026, 3, 10), new(2026, 3, 12)],
            dates);
    }

    [Fact]
    public void Both_ends_of_the_window_are_included()
    {
        var dates = TrainingSchedule.DatesIn(
            new DateTime(2026, 3, 3), new DateTime(2026, 3, 10), [DayOfWeek.Tuesday]);

        Assert.Equal([new(2026, 3, 3), new(2026, 3, 10)], dates);
    }

    [Fact]
    public void The_time_of_day_on_either_end_is_dropped()
    {
        var dates = TrainingSchedule.DatesIn(
            new DateTime(2026, 3, 3, 19, 30, 0), new DateTime(2026, 3, 3, 21, 0, 0), [DayOfWeek.Tuesday]);

        // A session has no start time, and a window whose ends carried one would drop the day it opens on.
        Assert.Equal([new(2026, 3, 3)], dates);
    }

    [Fact]
    public void With_no_weekday_chosen_there_is_no_schedule()
    {
        // Not a session every day: a team that has not said when it trains has not asked for anything.
        Assert.Empty(TrainingSchedule.DatesIn(new DateTime(2026, 3, 2), new DateTime(2026, 6, 30), []));
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_yields_nothing()
    {
        Assert.Empty(TrainingSchedule.DatesIn(new DateTime(2026, 3, 10), new DateTime(2026, 3, 3), [DayOfWeek.Tuesday]));
    }

    [Fact]
    public void A_window_too_short_to_reach_the_chosen_day_yields_nothing()
    {
        Assert.Empty(TrainingSchedule.DatesIn(new DateTime(2026, 3, 4), new DateTime(2026, 3, 9), [DayOfWeek.Tuesday]));
    }
}
