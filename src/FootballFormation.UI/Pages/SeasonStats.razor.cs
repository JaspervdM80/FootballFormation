using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

public partial class SeasonStats
{
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private Core.Reporting.SeasonStats? _stats;
    private bool _loaded;

    private List<Core.Reporting.PlayerStats> _scorers = [];
    private List<Core.Reporting.PlayerStats> _keepers = [];
    private List<Core.Reporting.PlayerStats> _playingTime = [];

    protected override async Task LoadAsync()
    {
        // Back to the spinner while the newly selected season loads.
        _loaded = false;

        var result = await StatsService.GetSeasonAsync(SeasonId, Cancellation);
        var view = _notice.ReportFailure(L, result)
            ? result.Value!
            : new SeasonStatsView(Core.Reporting.SeasonStats.Empty, SeasonSquads.Empty);

        _stats = view.Stats;
        var squads = view.Squads;

        _scorers = _stats.Players
            .Where(p => p.Goals > 0 || p.Assists > 0)
            .OrderByDescending(p => p.Goals)
            .ThenByDescending(p => p.Assists)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _keepers = _stats.Players
            .Where(p => p.GoalkeeperMinutes > 0)
            .OrderByDescending(p => p.GoalkeeperMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        // Guests are out because this is about squad rotation. Ordered by share rather than volume: someone who missed half the season
        // and played every minute of the rest was not rotated out, though a total would say so. Minutes break the whole-percent ties.
        _playingTime = _stats.Players
            .Where(p => squads.IsFullMemberAnywhere(p.Player.Id))
            .OrderByDescending(p => p.Utilization)
            .ThenByDescending(p => p.TotalMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _loaded = true;
    }

    /// Single-letter form pill, localized (W/D/L in English, W/G/V in Dutch).
    private string ResultLetter(GameResult r) => L[r.ToString()].ToString()[..1];

    /// The availability bar's four segments, in the order they are stacked.
    private (string Class, string Label)[] Legend =>
    [
        ("pt-played", L["Played"]),
        ("pt-injured", L["Injured"]),
        ("pt-unavailable", L["Unavailable"]),
        ("pt-idle", L["Not played"])
    ];

    /// The exact figure a colour only approximates. On a phone the legend names it instead, since a title attribute never surfaces.
    private static string Figure(string label, int minutes) => $"{label}: {minutes}'";
}
