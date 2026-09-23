namespace FootballFormation.Core.Reporting;

public enum DutyTiming
{
    Past,
    Next,
    Upcoming
}

public record DutyRow(Game Game, DutyTiming Timing);

/// A column is shown only when some game in the list fills it in, so a duty the team does not keep reads as absent, not as a row of dashes.
public record DutyRoster(List<DutyRow> Rows, bool ShowsDressingRoom, bool ShowsFlags, bool ShowsWash)
{
    public bool HasAnyDuty => ShowsDressingRoom || ShowsFlags || ShowsWash;
}

public static class DutyRosterReport
{
    /// The duties are free text, so the same family can be written two ways; they are listed as entered, never grouped by person.
    public static DutyRoster Build(IEnumerable<Game> games, DateTime today)
    {
        var ordered = games.OldestFirst();

        // By the calendar rather than the scoreline: whoever washes the kit on a match day is still on duty after the final whistle.
        var next = ordered.FirstOrDefault(g => g.Date.Date >= today.Date);

        var rows = ordered
            .Select(g => new DutyRow(g, g == next ? DutyTiming.Next : g.Date.Date < today.Date ? DutyTiming.Past : DutyTiming.Upcoming))
            .ToList();

        return new DutyRoster(
            rows,
            ordered.Any(g => IsFilled(g.DressingRoomDuty)),
            ordered.Any(g => IsFilled(g.FlagDuty)),
            ordered.Any(g => IsFilled(g.WashDuty)));
    }

    private static bool IsFilled(string? duty) => !string.IsNullOrWhiteSpace(duty);
}
