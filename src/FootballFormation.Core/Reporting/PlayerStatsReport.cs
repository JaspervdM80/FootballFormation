namespace FootballFormation.Core.Reporting;

public class PositionStat
{
    public required PlayerPosition Position { get; init; }
    public int Minutes { get; init; }

    /// <summary>Share of the player's total minutes, 0–100.</summary>
    public double Percentage { get; init; }
}

public class PlayerGameStat
{
    public required Game Game { get; init; }
    public int Minutes { get; init; }
    public int Goals { get; init; }
    public int Assists { get; init; }

    /// <summary>False when the minutes are the planned estimate because the game was never run
    /// live. See <see cref="GameMinutesReport"/>.</summary>
    public bool IsActual { get; init; }

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

    public int GoalkeeperMinutes { get; init; }

    /// <summary>Minutes the player was available to play — the played duration of every game
    /// they were in the roster for (with a lineup). Games they were unavailable for don't
    /// count, and a game they were hurt in counts only up to the injury, so this is a fair
    /// denominator: on-pitch minutes vs. bench time they could have been called off.</summary>
    public int AvailableMinutes { get; init; }

    /// <summary>Minutes injury cost her: the whole of a match <see cref="Game.InjuredPlayerIds"/>
    /// records her missing, and the stretch from the injury to the final whistle in one she was
    /// carried off in.</summary>
    public int InjuredMinutes { get; init; }

    /// <summary>Minutes of matches she was left out of the roster for, injury aside.</summary>
    public int UnavailableMinutes { get; init; }

    public int Goals { get; init; }
    public int Assists { get; init; }

    public required List<PositionStat> Positions { get; init; }

    /// <summary>Per-game breakdown, newest first — only games the player took part in.</summary>
    public required List<PlayerGameStat> Games { get; init; }

    public int GoalContributions => Goals + Assists;

    /// <summary>Available minutes spent off the pitch. Floored at zero for the one case that can
    /// go negative: a player marked unavailable for a game she was picked for anyway.</summary>
    public int NotPlayedMinutes => Math.Max(0, AvailableMinutes - TotalMinutes);

    /// <summary>
    /// Every minute the games covered had to offer. The four figures behind it — played, not played,
    /// injured, unavailable — partition it, except where somebody was picked for a match she was
    /// marked out of (see <see cref="NotPlayedMinutes"/>); and because each game contributes its
    /// whole duration to exactly one of them, this comes out the same for every squad member of the
    /// season. That is what lets the availability bars on /stats be read against each other rather
    /// than each against itself — and why the switch offering them is not shown on "All seasons",
    /// where a player who joined late shares no history with one who did not.
    /// </summary>
    public int MaximumMinutes => AvailableMinutes + InjuredMinutes + UnavailableMinutes;

    /// <summary>Share of available minutes actually spent on the pitch, 0–100.</summary>
    public double Utilization => AvailableMinutes > 0 ? Math.Round((double)TotalMinutes / AvailableMinutes * 100, 0) : 0;

    /// <summary>Share of <see cref="MaximumMinutes"/> spent on the pitch, 0–100.</summary>
    public double Availability => MaximumMinutes > 0 ? Math.Round((double)TotalMinutes / MaximumMinutes * 100, 0) : 0;

    public double AverageMinutes => GamesPlayed > 0 ? (double)TotalMinutes / GamesPlayed : 0;
    public double GoalsPerGame => GamesPlayed > 0 ? (double)Goals / GamesPlayed : 0;
    public double AssistsPerGame => GamesPlayed > 0 ? (double)Assists / GamesPlayed : 0;
    public double ContributionsPerGame => GamesPlayed > 0 ? (double)GoalContributions / GamesPlayed : 0;
}

/// <summary>
/// Turns a player's game history into aggregate stats. Pure computation — no state, no
/// service calls, and no opinion about scope: the caller decides which games to pass in, which
/// is how the same builder serves both season and career figures.
/// <para>
/// Only games that are <see cref="Game.IsComplete"/> contribute anything — minutes, positions,
/// goals, assists or available minutes. A match still being played leaves every figure untouched
/// until the final whistle.
/// </para>
/// <para>
/// Minutes and positions come from <see cref="GameMinutesReport"/>, which reads the real timings
/// and substitutions of a game that was run live and only falls back to the planned lineup for
/// one that was not.
/// </para>
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

        // Seconds, not minutes, for every accumulator below: real timings rarely land on a whole
        // minute, and rounding each game separately can disagree with rounding the total once —
        // see Game.SecondsToMinutes. Each figure is converted to minutes exactly once, at the end.
        var positionSeconds = new Dictionary<PlayerPosition, int>();
        var availableSeconds = 0;
        var injuredSeconds = 0;
        var unavailableSeconds = 0;

        foreach (var game in games)
        {
            // A game in progress contributes nothing at all until it has been played out.
            if (!game.IsComplete) continue;

            // A game with a lineup gives up its whole duration, to one bucket or split across two.
            // Available = in the roster, whether they started, subbed, or sat the bench; a game
            // they were hurt in counts only up to the injury — see Game.AvailableSecondsFor — and
            // the rest of it is time they could not have played. Out of the roster, the game says
            // which of the two reasons it was.
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
            // Newest first is this list's contract, not something to inherit from the caller.
            Games = [.. gameStats.OrderByDescending(g => g.Game.Date).ThenBy(g => g.Game.Id)]
        };
    }
}
