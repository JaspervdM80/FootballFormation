namespace FootballFormation.Core.Tests;

/// A session's date carries its start time in the same field, so "no time entered" has to be a value rather than a null — the same trick
/// Game plays, and the same one that goes wrong quietly if the check drifts.
public class TrainingTests
{
    [Fact]
    public void A_session_at_midnight_is_one_with_no_start_time_entered()
    {
        var training = new Training { Date = new DateTime(2026, 3, 17) };

        Assert.False(training.HasStartTime);
        Assert.Equal("17 Mar", training.DateLine("dd MMM"));
    }

    [Fact]
    public void A_session_with_a_start_time_says_so_on_the_same_line()
    {
        var training = new Training { Date = new DateTime(2026, 3, 17, 19, 30, 0) };

        Assert.True(training.HasStartTime);
        Assert.Equal("17 Mar, 19:30", training.DateLine("dd MMM"));
    }

    [Fact]
    public void Two_sessions_on_one_day_keep_the_order_they_were_entered_in()
    {
        var day = new DateTime(2026, 3, 17);
        var trainings = new List<Training>
        {
            new() { Id = 2, Date = day },
            new() { Id = 1, Date = day },
            new() { Id = 3, Date = day.AddDays(1) },
        };

        // Newest day first, and within it the id — without that tie-break the two same-day rows would come back in whatever order the
        // sort happened to leave them.
        Assert.Equal([3, 1, 2], trainings.NewestFirst().Select(t => t.Id));
    }
}
