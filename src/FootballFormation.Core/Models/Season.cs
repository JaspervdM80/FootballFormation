namespace FootballFormation.Core.Models;

public class Season
{
    /// The KNVB amateur season, 1 July – 30 June. Gapless windows within a team are what let <see cref="Game.SeasonId"/> be required:
    /// every date maps to exactly one of that team's seasons, so a game can never end up orphaned.
    public const int StartMonth = 7;

    public int Id { get; set; }

    /// The team this season belongs to, and the root every piece of season-scoped data reaches its team through — Game, Training,
    /// MatchPreferences and the squad each carry a copy so the team query filter can read one column without a join back to here.
    public int TeamId { get; set; }

    /// Derived by <see cref="NameForStartYear"/> at creation but editable, so a club can write "2025/26 (najaar)".
    public required string Name { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// Exactly one row per team carries this, and SeasonService.SetCurrentAsync owns that invariant.
    public bool IsCurrent { get; set; }

    public List<Game> Games { get; set; } = [];

    public List<SeasonSquadMember> SquadMembers { get; set; } = [];

    /// Date-only, because a game's own date can carry a kick-off time while the window is a pair of calendar days.
    public bool Contains(DateTime date) => date.Date >= StartDate.Date && date.Date <= EndDate.Date;

    /// Short label for the crowded app bar and narrow screens: "25/26".
    public string ShortName => Name.Length > 5 && Name.Contains('/') ? Name[2..] : Name;

    /// Named by the opening year: both August 2025 and March 2026 fall in 2025/26.
    public static int StartYearFor(DateTime date) =>
        date.Month >= StartMonth ? date.Year : date.Year - 1;

    public static string NameForStartYear(int startYear) =>
        $"{startYear}/{(startYear + 1) % 100:D2}";

    /// A fresh, unsaved season covering <paramref name="date"/>.
    public static Season CreateFor(DateTime date)
    {
        var startYear = StartYearFor(date);

        return new Season
        {
            Name = NameForStartYear(startYear),
            StartDate = new DateTime(startYear, StartMonth, 1),
            EndDate = new DateTime(startYear + 1, StartMonth, 1).AddDays(-1)
        };
    }
}

/// In memory, the same rule as <see cref="GameOrdering"/>. The table holds one row a year, so SeasonService reads it whole and keeps its
/// window arithmetic in memory too — which is what lets <see cref="Season.Contains"/> be the single definition of a window.
public static class SeasonOrdering
{
    public static List<Season> NewestFirst(this IEnumerable<Season> seasons) =>
        [.. seasons.OrderByDescending(s => s.StartDate).ThenBy(s => s.Id)];

    /// Oldest first — the order the gap checks walk the windows in.
    public static List<Season> OldestFirst(this IEnumerable<Season> seasons) =>
        [.. seasons.OrderBy(s => s.StartDate).ThenBy(s => s.Id)];
}
