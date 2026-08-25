using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

public enum GameResult
{
    Win,
    Draw,
    Loss
}

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

    /// <summary>Per-player figures, one entry per squad member of the seasons covered (guests
    /// included; the page filters them out of the fairness table but keeps them in scorer lists).</summary>
    public required List<PlayerStats> Players { get; init; }

    public int GoalDifference => GoalsFor - GoalsAgainst;
    public double WinPercentage => Played > 0 ? Math.Round((double)Won / Played * 100, 0) : 0;

    // For a page whose load failed, so the markup needs no second shape to render.
    public static SeasonStats Empty { get; } = new() { Form = [], Players = [] };
}

/// <summary>
/// Aggregates a whole season into team totals plus per-player stats. Pure computation — no
/// state, no service calls. Team record and goals come from the authoritative scoreline;
/// per-player figures reuse <see cref="PlayerStatsReport"/> so minute/goal logic stays in one place.
/// </summary>
public static class SeasonStatsReport
{
    private const int FormLength = 5;

    /// <param name="squads">The squads of every season <paramref name="games"/> covers — forwarded
    /// to <see cref="PlayerStatsReport.Build"/>, which needs per-season guest status.</param>
    public static SeasonStats Build(IReadOnlyList<Player> players, IReadOnlyList<Game> games, SeasonSquads squads)
    {
        // IsComplete, not just "has a score": a match in progress has a running scoreline from its
        // first goal, and must not move the table, the form guide or the record until full time.
        var finished = games
            .Where(g => g.IsComplete && g.ScoreHome.HasValue && g.ScoreAway.HasValue)
            .ToList();

        var form = finished
            .NewestFirst()
            .Take(FormLength)
            .Select(ResultOf)
            .ToList();

        var playerStats = players
            .Select(p => PlayerStatsReport.Build(p, games, squads))
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
