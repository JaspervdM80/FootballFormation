namespace FootballFormation.Core.Models;

public class Season
{
    /// <summary>
    /// Month a season starts, matching the KNVB amateur season (1 July – 30 June). Keeping the
    /// windows gapless is what lets <see cref="Game.SeasonId"/> be required: every date maps to
    /// exactly one season, so a game can never end up orphaned.
    /// </summary>
    public const int StartMonth = 7;

    public int Id { get; set; }

    /// <summary>Display label, e.g. "2025/26". Derived by <see cref="NameForStartYear"/> when a
    /// season is created, but editable — a club may want something like "2025/26 (najaar)".</summary>
    public required string Name { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>The season the app defaults to. Exactly one row carries this;
    /// <c>SeasonService.SetCurrentAsync</c> owns that invariant.</summary>
    public bool IsCurrent { get; set; }

    public List<Game> Games { get; set; } = [];

    public List<SeasonSquadMember> SquadMembers { get; set; } = [];

    /// <summary>Whether <paramref name="date"/> falls inside this season's window. Date-only,
    /// because the window itself is a pair of calendar days — a game's own date can carry a
    /// kick-off time (<see cref="Game.HasStartTime"/>), which is exactly why comparing the two
    /// on anything finer than the day would be comparing different things.</summary>
    public bool Contains(DateTime date) => date.Date >= StartDate.Date && date.Date <= EndDate.Date;

    /// <summary>Short label for the crowded app bar and narrow screens: "25/26".</summary>
    public string ShortName => Name.Length > 5 && Name.Contains('/') ? Name[2..] : Name;

    /// <summary>The season a date belongs to, named by its opening year: both August 2025 and
    /// March 2026 fall in 2025/26.</summary>
    public static int StartYearFor(DateTime date) =>
        date.Month >= StartMonth ? date.Year : date.Year - 1;

    public static string NameForStartYear(int startYear) =>
        $"{startYear}/{(startYear + 1) % 100:D2}";

    /// <summary>A fresh, unsaved season covering <paramref name="date"/>.</summary>
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

/// <summary>
/// Ordering seasons by date in memory — the same rule as <see cref="GameOrdering"/>.
/// <para>
/// The table holds one row a year, so <c>SeasonService</c> reads all of it and does its window
/// arithmetic in memory too, which also lets <see cref="Season.Contains"/> be the single
/// definition of a window.
/// </para>
/// </summary>
public static class SeasonOrdering
{
    public static List<Season> NewestFirst(this IEnumerable<Season> seasons) =>
        [.. seasons.OrderByDescending(s => s.StartDate).ThenBy(s => s.Id)];

    /// <summary>Oldest first — the order the gap checks walk the windows in.</summary>
    public static List<Season> OldestFirst(this IEnumerable<Season> seasons) =>
        [.. seasons.OrderBy(s => s.StartDate).ThenBy(s => s.Id)];
}
