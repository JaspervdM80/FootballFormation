using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>Minutes and share a player spent in one position, over the games passed in.</summary>
public class PositionStat
{
    public required PlayerPosition Position { get; init; }
    public int Minutes { get; init; }

    /// <summary>Share of the player's total minutes, 0–100.</summary>
    public double Percentage { get; init; }
}

/// <summary>What one player did in one game.</summary>
public class PlayerGameStat
{
    public required Game Game { get; init; }
    public int Minutes { get; init; }
    public int Goals { get; init; }
    public int Assists { get; init; }

    public bool Played => Minutes > 0;
}

/// <summary>A player's aggregated figures over the games passed to
/// <see cref="PlayerStatsReport.Build"/> — one season's worth when the caller filtered by season,
/// career totals when it did not.</summary>
public class PlayerStats
{
    public required Player Player { get; init; }

    /// <summary>Games in which the player was on the pitch for at least one period.</summary>
    public int GamesPlayed { get; init; }
    public int TotalMinutes { get; init; }

    /// <summary>Minutes spent in goal (position GK).</summary>
    public int GoalkeeperMinutes { get; init; }

    /// <summary>Minutes the player was available to play — the full duration of every game
    /// they were in the roster for (with a lineup). Games they were unavailable for don't
    /// count, so this is a fair denominator: on-pitch minutes vs. bench/unavailable time.</summary>
    public int AvailableMinutes { get; init; }

    public int Goals { get; init; }
    public int Assists { get; init; }

    public required List<PositionStat> Positions { get; init; }

    /// <summary>Per-game breakdown, newest first — only games the player took part in.</summary>
    public required List<PlayerGameStat> Games { get; init; }

    public int GoalContributions => Goals + Assists;

    /// <summary>Share of available minutes actually spent on the pitch, 0–100.</summary>
    public double Utilization => AvailableMinutes > 0 ? Math.Round((double)TotalMinutes / AvailableMinutes * 100, 0) : 0;

    public double AverageMinutes => GamesPlayed > 0 ? (double)TotalMinutes / GamesPlayed : 0;
    public double GoalsPerGame => GamesPlayed > 0 ? (double)Goals / GamesPlayed : 0;
    public double AssistsPerGame => GamesPlayed > 0 ? (double)Assists / GamesPlayed : 0;
    public double ContributionsPerGame => GamesPlayed > 0 ? (double)GoalContributions / GamesPlayed : 0;
}

/// <summary>
/// Turns a player's game history into aggregate stats. Pure computation — no state, no
/// service calls, and no opinion about scope: the caller decides which games to pass in, which
/// is how the same builder serves both season and career figures. Minute logic mirrors
/// <see cref="PlayingTimeReport"/>: a player earns a period's minutes only when fielded (not a
/// substitute) in that period.
/// </summary>
public static class PlayerStatsReport
{
    /// <param name="squads">The squads of every season <paramref name="games"/> covers. Plural
    /// because guest status is per season and the caller may be showing "All seasons": each game
    /// resolves its own season, so a player who was a guest one year and a regular the next is
    /// judged correctly in each.</param>
    public static PlayerStats Build(Player player, IEnumerable<Game> games, SeasonSquads squads)
    {
        var gameStats = new List<PlayerGameStat>();
        var positionMinutes = new Dictionary<PlayerPosition, int>();
        var availableMinutes = 0;

        foreach (var game in games)
        {
            // Available = the player was in the roster for a game that actually has a lineup,
            // whether they started, subbed, or sat the bench. Unavailable games don't count.
            if (game.HasLineup && game.IsInRoster(player, squads))
                availableMinutes += game.GameDurationMinutes;

            var playedPeriods = 0;

            foreach (var period in game.Periods)
            {
                var entry = period.PlayerPositions
                    .FirstOrDefault(pp => pp.PlayerId == player.Id && !pp.IsSubstitute);
                if (entry is null) continue;

                playedPeriods++;
                positionMinutes[entry.Position] =
                    positionMinutes.GetValueOrDefault(entry.Position) + game.PeriodDurationMinutes;
            }

            var minutes = playedPeriods * game.PeriodDurationMinutes;

            // Own goals don't count towards the scorer's tally.
            var goals = game.Goals.Count(g => g.ScorerId == player.Id && !g.IsOwnGoal);
            var assists = game.Goals.Count(g => g.AssisterId == player.Id);

            // Skip games the player neither played nor scored/assisted in.
            if (minutes == 0 && goals == 0 && assists == 0) continue;

            gameStats.Add(new PlayerGameStat
            {
                Game = game,
                Minutes = minutes,
                Goals = goals,
                Assists = assists
            });
        }

        var totalMinutes = positionMinutes.Values.Sum();

        var positions = positionMinutes
            .Select(kv => new PositionStat
            {
                Position = kv.Key,
                Minutes = kv.Value,
                Percentage = totalMinutes > 0
                    ? Math.Round((double)kv.Value / totalMinutes * 100, 0)
                    : 0
            })
            .OrderByDescending(p => p.Minutes)
            .ThenBy(p => p.Position)
            .ToList();

        return new PlayerStats
        {
            Player = player,
            GamesPlayed = gameStats.Count(g => g.Played),
            TotalMinutes = totalMinutes,
            GoalkeeperMinutes = positionMinutes.GetValueOrDefault(PlayerPosition.GK),
            AvailableMinutes = availableMinutes,
            Goals = gameStats.Sum(g => g.Goals),
            Assists = gameStats.Sum(g => g.Assists),
            Positions = positions,
            Games = gameStats
        };
    }
}
