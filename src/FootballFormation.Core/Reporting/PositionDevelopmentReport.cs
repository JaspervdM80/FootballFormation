using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>One player's row in the squad-wide positions grid — every position they were on the
/// pitch in, keyed for the page to look up by column.</summary>
public class PositionDevelopmentRow
{
    public required Player Player { get; init; }
    public required IReadOnlyDictionary<PlayerPosition, PositionStat> Positions { get; init; }

    /// <summary>True when every minute this player has played this season came in one position —
    /// the squad-wide grid's whole reason to exist: who has never been asked to play anywhere else.</summary>
    public bool IsSinglePosition => Positions.Count == 1;
}

/// <summary>A squad-wide pivot of who has played where, for spotting a player stuck in one
/// position across a season rather than reading that off one player at a time.</summary>
public class PositionDevelopment
{
    /// <summary>Every position anyone in the squad played, in <see cref="PlayerPosition"/>'s
    /// declared order (goalkeeper, then defenders, midfielders, forwards) — the grid's columns.</summary>
    public required List<PlayerPosition> Positions { get; init; }

    public required List<PositionDevelopmentRow> Rows { get; init; }
}

/// <summary>
/// Pivots already-built per-player stats into a players × positions grid. Not a new aggregation:
/// <see cref="PlayerStats.Positions"/> already has the real minutes and share for every position a
/// player was on the pitch in, built by <see cref="PlayerStatsReport"/> from actual game data. This
/// just reshapes a list of those into one squad-wide table.
/// </summary>
public static class PositionDevelopmentReport
{
    /// <summary>Players with no minutes at all are left out — nothing to plot for them, and an
    /// empty row would just be noise in a grid meant to surface who has been narrowly used.</summary>
    public static PositionDevelopment Build(IEnumerable<PlayerStats> playerStats)
    {
        var rows = playerStats
            .Where(ps => ps.Positions.Count > 0)
            .Select(ps => new PositionDevelopmentRow
            {
                Player = ps.Player,
                Positions = ps.Positions.ToDictionary(p => p.Position, p => p)
            })
            .ToList();

        var positions = rows
            .SelectMany(r => r.Positions.Keys)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new PositionDevelopment { Positions = positions, Rows = rows };
    }
}
