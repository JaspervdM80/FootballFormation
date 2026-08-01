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

    /// <summary>Whether <paramref name="date"/> falls inside this season's window. Date-only,
    /// since game dates come from a date picker and carry a midnight time component.</summary>
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
