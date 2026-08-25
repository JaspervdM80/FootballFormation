using FootballFormation.Core.Models;
using FootballFormation.Core.Reporting;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FootballFormation.UI.Pages;

public partial class SeasonStats
{
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private readonly PageNotice _notice = new();

    private Core.Reporting.SeasonStats? _stats;
    private bool _loaded;

    // Derived views over _stats, computed once on load for the razor.
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

        // Fairness table is about squad rotation, so guests are left out — per season. On "All
        // seasons" a player counts if they were a regular in at least one of the seasons shown.
        // Ordered by share rather than by volume: someone who missed half the season and played
        // every minute of the rest was not rotated out, and sorting on the total says they were.
        // Utilization is whole percent, so ties are common and minutes break them.
        _playingTime = _stats.Players
            .Where(p => squads.IsFullMemberAnywhere(p.Player.Id))
            .OrderByDescending(p => p.Utilization)
            .ThenByDescending(p => p.TotalMinutes)
            .ThenBy(p => p.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        _loaded = true;
    }

    /// <summary>Single-letter form pill, localized (W/D/L in English, W/G/V in Dutch).</summary>
    private string ResultLetter(GameResult r) => L[r.ToString()].ToString()[..1];

    /// <summary>The availability bar's four segments, in the order they are stacked.</summary>
    private (string Class, string Label)[] Legend =>
    [
        ("pt-played", L["Played"]),
        ("pt-injured", L["Injured"]),
        ("pt-unavailable", L["Unavailable"]),
        ("pt-idle", L["Not played"])
    ];

    /// <summary>Segment tooltip: the exact figure a colour only approximates. The legend is what
    /// names it on a phone, where a title attribute never surfaces.</summary>
    private static string Figure(string label, int minutes) => $"{label}: {minutes}'";
}
