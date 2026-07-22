using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>Outcome of one finished game, from our perspective.</summary>
public enum GameResult
{
    Win,
    Draw,
    Loss
}

/// <summary>Team- and squad-wide figures for the season.</summary>
public class SeasonStats
{
    public int Played { get; init; }
    public int Won { get; init; }
    public int Drawn { get; init; }
    public int Lost { get; init; }

    /// <summary>Goals we scored — sum of <see cref="Game.ScoreHome"/> over finished games.</summary>
    public int GoalsFor { get; init; }

    /// <summary>Goals conceded — sum of <see cref="Game.ScoreAway"/> over finished games.</summary>
    public int GoalsAgainst { get; init; }

    /// <summary>Most recent finished games first, capped for a form guide.</summary>
    public required List<GameResult> Form { get; init; }

    /// <summary>Per-player season figures, one entry per player (guests included; the page
    /// filters them out of the fairness table but keeps them in scorer lists).</summary>
    public required List<PlayerStats> Players { get; init; }

    public int GoalDifference => GoalsFor - GoalsAgainst;
    public double WinPercentage => Played > 0 ? Math.Round((double)Won / Played * 100, 0) : 0;
}

/// <summary>
/// Aggregates a whole season into team totals plus per-player stats. Pure computation — no
/// state, no service calls. Team record and goals come from the authoritative scoreline;
/// per-player figures reuse <see cref="PlayerStatsReport"/> so minute/goal logic stays in one place.
/// </summary>
public static class SeasonStatsReport
{
    private const int FormLength = 5;

    public static SeasonStats Build(IReadOnlyList<Player> players, IReadOnlyList<Game> games)
    {
        var finished = games
            .Where(g => g.ScoreHome.HasValue && g.ScoreAway.HasValue)
            .ToList();

        var form = finished
            .OrderByDescending(g => g.Date)
            .Take(FormLength)
            .Select(ResultOf)
            .ToList();

        var playerStats = players
            .Select(p => PlayerStatsReport.Build(p, games))
            .ToList();

        return new SeasonStats
        {
            Played = finished.Count,
            Won = finished.Count(g => g.ScoreHome > g.ScoreAway),
            Drawn = finished.Count(g => g.ScoreHome == g.ScoreAway),
            Lost = finished.Count(g => g.ScoreHome < g.ScoreAway),
            GoalsFor = finished.Sum(g => g.ScoreHome!.Value),
            GoalsAgainst = finished.Sum(g => g.ScoreAway!.Value),
            Form = form,
            Players = playerStats
        };
    }

    private static GameResult ResultOf(Game g) =>
        g.ScoreHome > g.ScoreAway ? GameResult.Win
        : g.ScoreHome < g.ScoreAway ? GameResult.Loss
        : GameResult.Draw;
}
