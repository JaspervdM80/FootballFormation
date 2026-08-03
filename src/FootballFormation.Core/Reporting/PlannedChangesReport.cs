using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// A player coming off for one coming on. Either side can be null when the line-ups do not
/// balance — eleven out and ten in is a line-up worth flagging, not one worth hiding.
/// </summary>
public record PlannedSubstitution(Player? PlayerOff, Player? PlayerOn, PlayerPosition Position);

/// <summary>A player who stays on the pitch but in a different position.</summary>
public record PlannedMove(Player Player, PlayerPosition From, PlayerPosition To);

/// <summary>What the next line-up does: who is swapped, and who shifts position.</summary>
public record PlannedChanges(List<PlannedSubstitution> Substitutions, List<PlannedMove> Moves)
{
    public static readonly PlannedChanges None = new([], []);

    public bool IsEmpty => Substitutions.Count == 0 && Moves.Count == 0;
}

/// <summary>
/// The changes the planned line-ups imply. A quarters game is planned as two line-ups per half,
/// and the difference between them is exactly what is due midway through that half — so the live
/// screen can announce it without ever mentioning a quarter.
/// <para>
/// Substitutions and position moves are kept apart on purpose. Rewriting a back four commonly
/// touches every slot while only one player actually leaves the pitch, and a flat list of slot
/// differences buries that one substitution among six shuffles.
/// </para>
/// </summary>
public static class PlannedChangesReport
{
    /// <param name="current">The period being played. Live substitutions have already been
    /// applied to it, so the changes shown stay true to who is actually on the pitch.</param>
    /// <param name="next">The period whose line-up takes over.</param>
    /// <param name="findPlayer">Resolves an id to a player; unknown ids come back as null.</param>
    public static PlannedChanges Build(GamePeriod current, GamePeriod next, Func<int, Player?> findPlayer)
    {
        var before = StartersBySlot(current);
        var after = StartersBySlot(next);

        var beforeIds = before.Values.Select(p => p.PlayerId).ToHashSet();
        var afterIds = after.Values.Select(p => p.PlayerId).ToHashSet();

        // In slot order, so the list reads back to front like a team sheet rather than in
        // whatever order the lineup rows happen to have been stored.
        var leaving = before.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !afterIds.Contains(p.PlayerId)).ToList();
        var arriving = after.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !beforeIds.Contains(p.PlayerId)).ToList();

        return new PlannedChanges(
            PairUp(leaving, arriving, before, findPlayer),
            Moves(before, after, findPlayer));
    }

    /// <summary>
    /// Matches who goes off to who comes on. An arrival is paired with whoever held the slot they
    /// are taking, which is the swap a coach would call out; when that player is staying on the
    /// pitch — a shuffle rather than a straight swap — the next unpaired departure is used instead.
    /// </summary>
    private static List<PlannedSubstitution> PairUp(
        List<GamePlayerPosition> leaving,
        List<GamePlayerPosition> arriving,
        Dictionary<int, GamePlayerPosition> before,
        Func<int, Player?> findPlayer)
    {
        var unpaired = new List<GamePlayerPosition>(leaving);
        var substitutions = new List<PlannedSubstitution>();

        foreach (var on in arriving)
        {
            var predecessor = before.GetValueOrDefault(on.SlotIndex!.Value);
            var off = unpaired.FirstOrDefault(p => p.PlayerId == predecessor?.PlayerId)
                ?? unpaired.FirstOrDefault();

            if (off is not null) unpaired.Remove(off);

            substitutions.Add(new PlannedSubstitution(
                off is null ? null : findPlayer(off.PlayerId),
                findPlayer(on.PlayerId),
                on.Position));
        }

        // Anyone left over comes off with nobody named to replace them.
        substitutions.AddRange(unpaired.Select(off =>
            new PlannedSubstitution(findPlayer(off.PlayerId), null, off.Position)));

        return substitutions;
    }

    private static List<PlannedMove> Moves(
        Dictionary<int, GamePlayerPosition> before,
        Dictionary<int, GamePlayerPosition> after,
        Func<int, Player?> findPlayer)
    {
        var stayingBySlot = before.Values.ToDictionary(p => p.PlayerId, p => p);

        return [.. after.OrderBy(e => e.Key).Select(e => e.Value)
            .Select(on => (On: on, Off: stayingBySlot.GetValueOrDefault(on.PlayerId)))
            .Where(x => x.Off is not null && x.Off.Position != x.On.Position)
            .Select(x => (x.On, x.Off, Player: findPlayer(x.On.PlayerId)))
            .Where(x => x.Player is not null)
            .Select(x => new PlannedMove(x.Player!, x.Off!.Position, x.On.Position))];
    }

    /// <summary>
    /// The starters keyed by the slot they occupy. A slot can only be held once, but a lineup
    /// saved by an older build is not guaranteed to honour that, so the first entry wins rather
    /// than the lookup throwing on data that is already stored.
    /// </summary>
    private static Dictionary<int, GamePlayerPosition> StartersBySlot(GamePeriod period)
    {
        var bySlot = new Dictionary<int, GamePlayerPosition>();

        foreach (var position in period.PlayerPositions.Where(p => !p.IsSubstitute && p.SlotIndex is not null))
            bySlot.TryAdd(position.SlotIndex!.Value, position);

        return bySlot;
    }
}
