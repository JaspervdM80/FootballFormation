using System.Globalization;

namespace FootballFormation.Core.Models;

/// A training session on one date. Which weekdays the team trains, and between which dates, is a per-season setting
/// (<see cref="MatchPreferences.TrainingDays"/> and the training period) that MatchPreferencesService turns into these rows on save; one
/// entered by hand alongside them is an extra evening, and stays that way.
public class Training
{
    public int Id { get; set; }

    /// Date only — a session has no start time, so anything in TimeOfDay is a value the app can neither show nor edit.
    public DateTime Date { get; set; }

    /// Denormalised from the season so the team query filter reads one column, never a join. Set from the season at creation. See Season.TeamId.
    public int TeamId { get; set; }

    /// Derived from <see cref="Date"/> at creation (SeasonService.GetOrCreateForDateAsync), but reassignable afterwards. No navigation
    /// back, like GameInjury: nothing reads the season off a session, and a nav nobody includes is a null waiting to be trusted.
    public int SeasonId { get; set; }

    /// Squad members who were not there. Guests are not tracked: a training is the season's squad, and nobody else is expected. Always
    /// empty once <see cref="DidNotTakePlace"/> is set — TrainingService clears it, because a session nobody had is not one everybody
    /// missed and two facts that can disagree eventually do.
    public List<int> UnavailablePlayerIds { get; set; } = [];

    /// Frost, a holiday, a hall double-booked. The session stays on file so the week reads honestly; <see cref="Notes"/> says why.
    public bool DidNotTakePlace { get; set; }

    public string? Notes { get; set; }

    /// Generated from the season's training period rather than entered by hand. False the moment the dialog saves it — a session the
    /// coach has opened is the coach's, and rewriting the period must not take it away.
    public bool FromSchedule { get; set; }

    /// Been and gone, and it went ahead. Today's evening does not count yet: its register can still change before the whistle.
    public bool HasBeenHeld(DateTime today) => Date.Date < today.Date && !DidNotTakePlace;

    /// The only session the scheduler may remove: nothing has been recorded against it, so deleting it loses nothing.
    public bool IsUnusedSchedule =>
        FromSchedule && !DidNotTakePlace && UnavailablePlayerIds.Count == 0 && string.IsNullOrWhiteSpace(Notes);
}

/// In memory, never in SQL — see QueryTags.ComparesDatesInSql. The tie-break is spelled out so two sessions on one day keep entry order.
public static class TrainingOrdering
{
    /// This week and the weeks ahead first, soonest first; the weeks already behind us below them, most recent first. Whole ISO weeks on
    /// both counts, matching how the page groups them, so a week still running stays on top once its own sessions have been and gone.
    public static List<Training> UpcomingFirst(this IEnumerable<Training> trainings, DateTime today)
    {
        var thisMonday = MondayOf(today);
        var all = trainings.ToList();

        return
        [
            .. all.Where(t => MondayOf(t.Date) >= thisMonday)
                  .OrderBy(t => t.Date).ThenBy(t => t.Id),
            .. all.Where(t => MondayOf(t.Date) < thisMonday)
                  .OrderByDescending(t => MondayOf(t.Date)).ThenBy(t => t.Date).ThenBy(t => t.Id),
        ];
    }

    public static DateTime MondayOf(DateTime date) =>
        ISOWeek.ToDateTime(ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date), DayOfWeek.Monday);
}
