namespace FootballFormation.Core.Models;

/// A training session on one date. Which weekdays the team trains is a per-season setting
/// (<see cref="MatchPreferences.TrainingDays"/>) that seeds the date; the row itself is always created by hand, so a week off is simply a
/// session nobody entered.
public class Training
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

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

    /// <see cref="Date"/> carries both parts, so midnight is how "no start time entered" is stored.
    public bool HasStartTime => Date.TimeOfDay != TimeSpan.Zero;

    public string DateLine(string format) =>
        HasStartTime ? $"{Date.ToString(format)}, {Date:HH:mm}" : Date.ToString(format);
}

/// In memory, never in SQL — see QueryTags.ComparesDatesInSql. The tie-break is spelled out so two sessions on one day keep entry order.
/// No OldestFirst alongside it, unlike GameOrdering: this list only ever runs newest-first, and an unused ordering is one more thing to
/// keep honest.
public static class TrainingOrdering
{
    public static List<Training> NewestFirst(this IEnumerable<Training> trainings) =>
        [.. trainings.OrderByDescending(t => t.Date).ThenBy(t => t.Id)];
}
