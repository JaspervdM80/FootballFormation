namespace FootballFormation.Core.Reporting;

/// One player's row in the squad-wide positions grid, keyed for the page to look up by column.
public class PositionDevelopmentRow
{
    public required Player Player { get; init; }
    public required IReadOnlyDictionary<PlayerPosition, PositionStat> Positions { get; init; }

    /// The grid's whole reason to exist: who has never been asked to play anywhere else.
    public bool IsSinglePosition => Positions.Count == 1;
}

/// A squad-wide pivot of who has played where, for spotting a player stuck in one position across a season.
public class PositionDevelopment
{
    /// The grid's columns, in <see cref="PlayerPosition"/>'s declared order: goalkeeper, then defenders, midfielders, forwards.
    public required List<PlayerPosition> Positions { get; init; }

    /// Shirt number order. Who is in the list at all is the caller's choice; the page passes the season's full members only.
    public required List<PositionDevelopmentRow> Rows { get; init; }

    /// How many players the grid has anything to say about — everyone who took the pitch.
    public int PlayersUsed => Rows.Count;

    /// How many distinct positions the squad covered between them.
    public int PositionsUsed => Positions.Count;

    /// The figure the page exists to surface: players who have only ever played one position.
    public int SinglePositionPlayers => Rows.Count(r => r.IsSinglePosition);
}

/// A reshape, not a new aggregation: <see cref="PlayerStats.Positions"/> already holds the real minutes and share for every position.
public static class PositionDevelopmentReport
{
    /// Players with no countable minutes are left out — an empty row is noise in a grid meant to surface who has been narrowly used.
    public static PositionDevelopment Build(IEnumerable<PlayerStats> playerStats)
    {
        var rows = playerStats
            .Select(ps => new PositionDevelopmentRow
            {
                Player = ps.Player,
                // On the rounded minutes the grid prints, not the raw seconds: a twenty-second cameo would otherwise add a "0' 0%"
                // column and clear a single-position flag that is still true at the resolution anyone can see.
                Positions = ps.Positions.Where(p => p.Minutes > 0).ToDictionary(p => p.Position, p => p)
            })
            .Where(r => r.Positions.Count > 0)
            .OrderBy(r => r.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        var positions = rows
            .SelectMany(r => r.Positions.Keys)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        return new PositionDevelopment { Positions = positions, Rows = rows };
    }
}
