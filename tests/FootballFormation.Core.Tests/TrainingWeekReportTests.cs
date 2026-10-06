namespace FootballFormation.Core.Tests;

public class TrainingWeekReportTests
{
    /// Saturday; its ISO week opens on Monday 9 March.
    private static readonly DateTime Today = new(2026, 3, 14);

    private static List<Training> Weekly(DateTime first, int weeks, int firstId = 1) =>
        [.. Enumerable.Range(0, weeks).Select(i => new Training { Id = firstId + i, Date = first.AddDays(7 * i) })];

    [Fact]
    public void This_week_and_next_are_shown_and_the_rest_of_the_planned_season_is_folded_away()
    {
        var weeks = TrainingWeekReport.Build(Weekly(new DateTime(2026, 3, 10), 10), Today);

        Assert.Equal([new DateTime(2026, 3, 9), new DateTime(2026, 3, 16)], weeks.Next.Select(w => w.Monday));
        Assert.Equal(8, weeks.Later.Count);
        Assert.Equal(8, weeks.LaterCount);
        Assert.Empty(weeks.Past);
    }

    [Fact]
    public void The_weeks_already_over_are_never_folded_and_read_most_recent_first()
    {
        // These are the evenings whose register gets corrected, so they must not sit behind the rest of the season.
        var weeks = TrainingWeekReport.Build([.. Weekly(new DateTime(2026, 2, 3), 5, 1), .. Weekly(new DateTime(2026, 3, 10), 6, 100)], Today);

        Assert.Equal(
            [new DateTime(2026, 3, 2), new DateTime(2026, 2, 23), new DateTime(2026, 2, 16), new DateTime(2026, 2, 9), new DateTime(2026, 2, 2)],
            weeks.Past.Select(w => w.Monday));
        Assert.Equal(4, weeks.Later.Count);
    }

    [Fact]
    public void A_break_still_opens_on_the_first_two_weeks_that_have_sessions()
    {
        // Counting calendar weeks would show nothing at all over the winter break and fold away the first evenings back.
        var weeks = TrainingWeekReport.Build(Weekly(new DateTime(2026, 4, 7), 4), Today);

        Assert.Equal([new DateTime(2026, 4, 6), new DateTime(2026, 4, 13)], weeks.Next.Select(w => w.Monday));
        Assert.Equal(2, weeks.LaterCount);
    }

    [Fact]
    public void Two_sessions_in_one_week_share_a_group_and_count_twice_in_the_folded_total()
    {
        List<Training> trainings =
        [
            new() { Id = 1, Date = new DateTime(2026, 3, 10) },
            new() { Id = 2, Date = new DateTime(2026, 3, 17) },
            new() { Id = 3, Date = new DateTime(2026, 3, 26) },
            new() { Id = 4, Date = new DateTime(2026, 3, 24) },
        ];

        var weeks = TrainingWeekReport.Build(trainings, Today);

        var later = Assert.Single(weeks.Later);
        Assert.Equal([4, 3], later.Trainings.Select(t => t.Id));
        Assert.Equal(2, weeks.LaterCount);
    }

    [Fact]
    public void A_season_with_no_more_than_two_planned_weeks_has_nothing_to_fold()
    {
        var weeks = TrainingWeekReport.Build(Weekly(new DateTime(2026, 3, 10), 2), Today);

        Assert.Equal(2, weeks.Next.Count);
        Assert.Empty(weeks.Later);
    }
}
