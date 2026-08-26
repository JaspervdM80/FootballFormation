namespace FootballFormation.Core.Reporting;

/// Either side can be null when the line-ups do not balance — eleven out and ten in is worth flagging, not hiding.
public record PlannedSubstitution(Player? PlayerOff, Player? PlayerOn, PlayerPosition Position);

public record PlannedMove(Player Player, PlayerPosition From, PlayerPosition To);

/// The same swap in terms of line-up rows rather than players: a slot and a position change hands, and neither belongs to a name.
internal record PlannedSwap(GamePlayerPosition? Off, GamePlayerPosition? On);

public record PlannedChanges(List<PlannedSubstitution> Substitutions, List<PlannedMove> Moves)
{
    public static readonly PlannedChanges None = new([], []);

    public bool IsEmpty => Substitutions.Count == 0 && Moves.Count == 0;
}

/// Substitutions and position moves are kept apart on purpose: rewriting a back four touches every slot while only one player leaves the
/// pitch, and a flat list of slot differences buries that one substitution among six shuffles.
public static class PlannedChangesReport
{
    /// Live substitutions are already applied to <paramref name="half"/>, and <paramref name="liveChanges"/> is what decides which swaps
    /// are still worth showing — see <see cref="KickOffStarters"/>.
    public static PlannedChanges Build(
        GamePeriod half,
        GamePeriod plan,
        Func<int, Player?> findPlayer,
        IEnumerable<GameSubstitution> liveChanges)
    {
        var before = StartersBySlot(half);
        var after = StartersBySlot(plan);

        return new PlannedChanges(
            [.. PairUp(before, after, KickOffStarters(before.Values, liveChanges))
                .Select(swap => Name(swap, findPlayer))],
            Moves(before, after, findPlayer));
    }

    /// The position is the one being taken over, which for a player nobody is named to replace is the one she is vacating.
    private static PlannedSubstitution Name(PlannedSwap swap, Func<int, Player?> findPlayer) =>
        new(swap.Off is null ? null : findPlayer(swap.Off.PlayerId),
            swap.On is null ? null : findPlayer(swap.On.PlayerId),
            (swap.On ?? swap.Off)!.Position);

    /// Once the touchline has already taken the named player off, the swap is about whoever inherited her slot — not what was planned.
    /// A swap with nobody named to come off is kept, because an unbalanced line-up is worth flagging.
    private static bool IsStillViable(PlannedSwap swap, HashSet<int> kickOffStarters) =>
        swap.Off is null || kickOffStarters.Contains(swap.Off.PlayerId);

    /// The line-up records where everyone stands now, so rewinding is the only way back to the eleven the plan was written against — the
    /// same walk <see cref="GameMinutesReport"/> makes.
    private static HashSet<int> KickOffStarters(
        IEnumerable<GamePlayerPosition> onPitchNow, IEnumerable<GameSubstitution> liveChanges)
    {
        var starters = onPitchNow.Select(p => p.PlayerId).ToHashSet();

        // Newest first, so a slot changing hands twice unwinds through whoever held it in between. The id settles a double substitution,
        // where both changes share a second.
        foreach (var sub in liveChanges.OrderByDescending(s => s.AtSeconds).ThenByDescending(s => s.Id))
        {
            starters.Remove(sub.PlayerOnId);
            starters.Add(sub.PlayerOffId);
        }

        return starters;
    }

    /// An arrival pairs with whoever held the slot she is taking, which is the swap a coach would call out; when that player is staying
    /// on — a shuffle rather than a straight swap — the next unpaired departure is used instead.
    private static List<PlannedSwap> PairUp(
        Dictionary<int, GamePlayerPosition> before,
        Dictionary<int, GamePlayerPosition> after,
        HashSet<int> kickOffStarters)
    {
        var beforeIds = before.Values.Select(p => p.PlayerId).ToHashSet();
        var afterIds = after.Values.Select(p => p.PlayerId).ToHashSet();

        // Slot order, so the list reads like a team sheet rather than in whatever order the line-up rows were stored.
        var leaving = before.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !afterIds.Contains(p.PlayerId)).ToList();
        var arriving = after.OrderBy(e => e.Key).Select(e => e.Value)
            .Where(p => !beforeIds.Contains(p.PlayerId)).ToList();

        var unpaired = new List<GamePlayerPosition>(leaving);
        var swaps = new List<PlannedSwap>();

        foreach (var on in arriving)
        {
            var predecessor = before.GetValueOrDefault(on.SlotIndex!.Value);
            var off = unpaired.FirstOrDefault(p => p.PlayerId == predecessor?.PlayerId)
                ?? unpaired.FirstOrDefault();

            if (off is not null) unpaired.Remove(off);

            swaps.Add(new PlannedSwap(off, on));
        }

        // Anyone left over comes off with nobody named to replace them.
        swaps.AddRange(unpaired.Select(off => new PlannedSwap(off, null)));

        return [.. swaps.Where(swap => IsStillViable(swap, kickOffStarters))];
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

    /// TryAdd rather than Add: a slot can only be held once, but a line-up saved by an older build is not guaranteed to honour that, and
    /// throwing on data already stored helps nobody.
    private static Dictionary<int, GamePlayerPosition> StartersBySlot(GamePeriod lineup)
    {
        var bySlot = new Dictionary<int, GamePlayerPosition>();

        foreach (var position in lineup.PlayerPositions.Where(p => !p.IsSubstitute && p.SlotIndex is not null))
            bySlot.TryAdd(position.SlotIndex!.Value, position);

        return bySlot;
    }
}
