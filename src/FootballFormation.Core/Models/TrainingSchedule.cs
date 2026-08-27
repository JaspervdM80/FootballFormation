namespace FootballFormation.Core.Models;

/// The dates a season's training period implies. Pure, like the report builders: the sessions it turns into are
/// MatchPreferencesService's business.
public static class TrainingSchedule
{
    /// Every date in the window falling on one of <paramref name="days"/>, ascending. Empty when no day is chosen — a team with no
    /// training days has no schedule, not a session every day.
    public static List<DateTime> DatesIn(DateTime firstDate, DateTime lastDate, IReadOnlyList<DayOfWeek> days)
    {
        var start = firstDate.Date;
        var end = lastDate.Date;

        if (days.Count == 0 || end < start) return [];

        return [.. Enumerable.Range(0, (end - start).Days + 1)
            .Select(offset => start.AddDays(offset))
            .Where(date => days.Contains(date.DayOfWeek))];
    }
}
