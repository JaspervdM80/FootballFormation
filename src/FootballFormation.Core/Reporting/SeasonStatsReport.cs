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

    /// Goals we scored — <see cref="Game.ScoreHome"/> is always ours, whatever the venue.
    public int GoalsFor { get; init; }

    /// Goals conceded — <see cref="Game.ScoreAway"/> is always theirs, whatever the venue.
    public int GoalsAgainst { get; init; }

    /// Most recent finished games first, capped for a form guide.
    public required List<GameResult> Form { get; init; }

    /// Guests included — the page filters them out of the fairness table but keeps them in scorer lists.
    public required List<PlayerStats> Players { get; init; }

    public int GoalDifference => GoalsFor - GoalsAgainst;
    public double WinPercentage => Played > 0 ? Math.Round((double)Won / Played * 100, 0) : 0;

    // For a page whose load failed, so the markup needs no second shape to render.
    public static SeasonStats Empty { get; } = new() { Form = [], Players = [] };
}

/// Team record and goals come from the stored scoreline; per-player figures go through <see cref="PlayerStatsReport"/>, so the minute
/// and goal rules stay in one place.
public static class SeasonStatsReport
{
    private const int FormLength = 5;

    public static SeasonStats Build(IReadOnlyList<Player> players, IReadOnlyList<Game> games, SeasonSquads squads)
    {
        // IsComplete, not just "has a score": a match in progress has a running scoreline from its first goal, and must not move the
        // table until full time.
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
