namespace FootballFormation.Core.Reporting;

public sealed record TrainingWeek(DateTime Monday, List<Training> Trainings);

// Past runs most recent week first.
public sealed record TrainingWeeks(List<TrainingWeek> Next, List<TrainingWeek> Later, List<TrainingWeek> Past)
{
    public int LaterCount => Later.Sum(week => week.Trainings.Count);
}

public static class TrainingWeekReport
{
    // Planned weeks rather than calendar weeks, so a winter break or a season yet to start still opens on its first sessions.
    public const int PlannedWeeksShown = 2;

    public static TrainingWeeks Build(IEnumerable<Training> trainings, DateTime today)
    {
        var thisMonday = TrainingOrdering.MondayOf(today);
        var weeks = trainings.UpcomingFirst(today)
            .GroupBy(t => TrainingOrdering.MondayOf(t.Date))
            .Select(week => new TrainingWeek(week.Key, [.. week]))
            .ToList();

        var planned = weeks.Where(week => week.Monday >= thisMonday).ToList();
        return new(
            [.. planned.Take(PlannedWeeksShown)],
            [.. planned.Skip(PlannedWeeksShown)],
            [.. weeks.Where(week => week.Monday < thisMonday)]);
    }
}
