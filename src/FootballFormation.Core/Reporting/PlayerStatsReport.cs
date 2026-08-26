namespace FootballFormation.Core.Reporting;

public class PositionStat
{
    public required PlayerPosition Position { get; init; }
    public int Minutes { get; init; }

    /// Share of the player's total minutes, 0–100.
    public double Percentage { get; init; }
}

public class PlayerGameStat
{
    public required Game Game { get; init; }
    public int Minutes { get; init; }
    public int Goals { get; init; }
    public int Assists { get; init; }

    /// False when the minutes are the planned estimate — see <see cref="GameMinutesReport"/>.
    public bool IsActual { get; init; }

    public bool Played => Minutes > 0;
}

/// Scope is whatever the caller passed to <see cref="PlayerStatsReport.Build"/>: one season, or a career.
public class PlayerStats
{
    public required Player Player { get; init; }

    /// Counts only games the player was actually on the pitch in.
    public int GamesPlayed { get; init; }
    public int TotalMinutes { get; init; }

    public int GoalkeeperMinutes { get; init; }

    /// A game she was hurt in counts only up to the injury, which is what makes this a fair denominator for <see cref="Utilization"/>.
    public int AvailableMinutes { get; init; }

    /// The whole of a match <see cref="Game.InjuredPlayerIds"/> records her missing, or the stretch from the injury to the final whistle.
    public int InjuredMinutes { get; init; }

    /// Minutes of matches she was left out of the roster for, injury aside.
    public int UnavailableMinutes { get; init; }

    public int Goals { get; init; }
    public int Assists { get; init; }

    public required List<PositionStat> Positions { get; init; }

    /// Newest first, and only games the player took part in.
    public required List<PlayerGameStat> Games { get; init; }

    public int GoalContributions => Goals + Assists;

    /// Floored at zero for the one case that can go negative: a player marked unavailable for a game she was picked for anyway.
    public int NotPlayedMinutes => Math.Max(0, AvailableMinutes - TotalMinutes);

    /// Every game contributes its whole duration to exactly one of the three, so this comes out the same for every squad member of a
    /// season — which is what lets the /stats availability bars be read against each other, and why they are hidden on "All seasons".
    public int MaximumMinutes => AvailableMinutes + InjuredMinutes + UnavailableMinutes;

    /// Share of available minutes actually spent on the pitch, 0–100.
    public double Utilization => AvailableMinutes > 0 ? Math.Round((double)TotalMinutes / AvailableMinutes * 100, 0) : 0;

    /// Share of <see cref="MaximumMinutes"/> spent on the pitch, 0–100.
    public double Availability => MaximumMinutes > 0 ? Math.Round((double)TotalMinutes / MaximumMinutes * 100, 0) : 0;

    public double AverageMinutes => GamesPlayed > 0 ? (double)TotalMinutes / GamesPlayed : 0;
    public double GoalsPerGame => GamesPlayed > 0 ? (double)Goals / GamesPlayed : 0;
    public double AssistsPerGame => GamesPlayed > 0 ? (double)Assists / GamesPlayed : 0;
    public double ContributionsPerGame => GamesPlayed > 0 ? (double)GoalContributions / GamesPlayed : 0;
}

/// No opinion about scope: the caller decides which games to pass in, which is how the same builder serves both season and career figures.
public static class PlayerStatsReport
{
    /// <paramref name="squads"/> is plural because guest status is per season: each game resolves its own, so "All seasons" stays correct.
    public static PlayerStats Build(Player player, IEnumerable<Game> games, SeasonSquads squads)
    {
        var gameStats = new List<PlayerGameStat>();

        // Seconds, not minutes, for every accumulator below: rounding each game separately can disagree with rounding the total once, so
        // each figure is converted exactly once at the end. See Game.SecondsToMinutes.
        var positionSeconds = new Dictionary<PlayerPosition, int>();
        var availableSeconds = 0;
        var injuredSeconds = 0;
        var unavailableSeconds = 0;

        foreach (var game in games)
        {
            if (!game.IsComplete) continue;

            // Every game with a line-up gives up its whole duration, to one bucket or split across two — which is what makes
            // MaximumMinutes come out the same for the whole squad.
            if (game.HasLineup)
            {
                if (game.IsInRoster(player, squads))
                {
                    var available = game.AvailableSecondsFor(player.Id);
                    availableSeconds += available;
                    injuredSeconds += game.PlayedDurationSecondsEffective - available;
                }
                else if (game.InjuredPlayerIds.Contains(player.Id))
                {
                    injuredSeconds += game.PlayedDurationSecondsEffective;
                }
                else
                {
                    unavailableSeconds += game.PlayedDurationSecondsEffective;
                }
            }

            var gameMinutes = GameMinutesReport.Build(game);
            var seconds = 0;

            foreach (var (position, span) in gameMinutes.PositionsFor(player.Id))
            {
                positionSeconds[position] = positionSeconds.GetValueOrDefault(position) + span;
                seconds += span;
            }

            var minutes = Game.SecondsToMinutes(seconds);

            // An own goal does not count towards the scorer's tally.
            var goals = game.Goals.Count(g => g.ScorerId == player.Id && !g.IsOwnGoal);
            var assists = game.Goals.Count(g => g.AssisterId == player.Id);

            if (minutes == 0 && goals == 0 && assists == 0) continue;

            gameStats.Add(new PlayerGameStat
            {
                Game = game,
                Minutes = minutes,
                Goals = goals,
                Assists = assists,
                IsActual = gameMinutes.IsActual
            });
        }

        var totalSeconds = positionSeconds.Values.Sum();
        var totalMinutes = Game.SecondsToMinutes(totalSeconds);

        var positions = positionSeconds
            .Select(kv => new PositionStat
            {
                Position = kv.Key,
                Minutes = Game.SecondsToMinutes(kv.Value),
                // From seconds, so the share is exact even where the rounded minutes are not.
                Percentage = totalSeconds > 0
                    ? Math.Round((double)kv.Value / totalSeconds * 100, 0)
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
            GoalkeeperMinutes = Game.SecondsToMinutes(positionSeconds.GetValueOrDefault(PlayerPosition.GK)),
            AvailableMinutes = Game.SecondsToMinutes(availableSeconds),
            InjuredMinutes = Game.SecondsToMinutes(injuredSeconds),
            UnavailableMinutes = Game.SecondsToMinutes(unavailableSeconds),
            Goals = gameStats.Sum(g => g.Goals),
            Assists = gameStats.Sum(g => g.Assists),
            Positions = positions,
            // Newest first is this list's contract, not something to inherit from the caller's ordering.
            Games = [.. gameStats.OrderByDescending(g => g.Game.Date).ThenBy(g => g.Game.Id)]
        };
    }
}
