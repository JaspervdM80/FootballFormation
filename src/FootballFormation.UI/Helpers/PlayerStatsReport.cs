using FootballFormation.Core.Models;

namespace FootballFormation.UI.Helpers;

/// <summary>Minutes and share a player spent in one position, across their whole history.</summary>
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

/// <summary>A player's aggregated career figures across every recorded game.</summary>
public class PlayerStats
{
    public required Player Player { get; init; }

    /// <summary>Games in which the player was on the pitch for at least one period.</summary>
    public int GamesPlayed { get; init; }
    public int TotalMinutes { get; init; }

    public int Goals { get; init; }
    public int Assists { get; init; }

    public required List<PositionStat> Positions { get; init; }

    /// <summary>Per-game breakdown, newest first — only games the player took part in.</summary>
    public required List<PlayerGameStat> Games { get; init; }

    public int GoalContributions => Goals + Assists;

    public double AverageMinutes => GamesPlayed > 0 ? (double)TotalMinutes / GamesPlayed : 0;
    public double GoalsPerGame => GamesPlayed > 0 ? (double)Goals / GamesPlayed : 0;
    public double AssistsPerGame => GamesPlayed > 0 ? (double)Assists / GamesPlayed : 0;
    public double ContributionsPerGame => GamesPlayed > 0 ? (double)GoalContributions / GamesPlayed : 0;
}

/// <summary>
/// Turns a player's game history into career stats. Pure computation — no state, no
/// service calls. Minute logic mirrors <see cref="PlayingTimeReport"/>: a player earns a
/// period's minutes only when fielded (not a substitute) in that period.
/// </summary>
public static class PlayerStatsReport
{
    public static PlayerStats Build(Player player, IEnumerable<Game> games)
    {
        var gameStats = new List<PlayerGameStat>();
        var positionMinutes = new Dictionary<PlayerPosition, int>();

        foreach (var game in games)
        {
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
            Goals = gameStats.Sum(g => g.Goals),
            Assists = gameStats.Sum(g => g.Assists),
            Positions = positions,
            Games = gameStats
        };
    }
}
