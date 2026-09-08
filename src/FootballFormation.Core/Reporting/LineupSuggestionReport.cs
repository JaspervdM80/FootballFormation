namespace FootballFormation.Core.Reporting;

public class SuggestedPlacement
{
    public required Player Player { get; init; }
    public required int SlotIndex { get; init; }
    public required PlayerPosition Position { get; init; }
    public required PositionFit Fit { get; init; }
}

public class LineupSuggestion
{
    /// Ordered by slot, so slot 0 is always the goalkeeper — see <see cref="Models.FormationSlots.For"/>.
    public required List<SuggestedPlacement> Starters { get; init; }

    /// Everyone handed in who did not make the eleven.
    public required List<Player> Substitutes { get; init; }
}

/// Picks who starts and where from two things: how well a player fits the slot, and how much she has already played. Minutes arrive
/// totalled, so the caller decides whether that means this season, this match or both.
public static class LineupSuggestionReport
{
    /// What the busiest player in the squad pays for being the busiest, in the same units as the fit penalties below. Above the cost of
    /// an alternative position and well under the cost of playing somebody nowhere near her position.
    private const double RestWeight = 10;

    /// A keeper out of position loses matches in a way a winger on the wrong flank does not.
    private const double GoalkeeperMultiplier = 2;

    private const double Tolerance = 1e-9;

    public static LineupSuggestion Build(
        PlayerPosition[] slots,
        IEnumerable<Player> candidates,
        IReadOnlyDictionary<int, int> minutesPlayed)
    {
        // Shirt then name: two equally rested players who fit a slot equally well must not swap places between two runs.
        var players = candidates
            .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
            .ThenBy(p => p.DisplayName)
            .ToList();

        if (players.Count == 0 || slots.Length == 0)
            return new LineupSuggestion { Starters = [], Substitutes = players };

        var costs = BuildCosts(slots, players, minutesPlayed);
        var slotOwners = Assign(costs, slots.Length, players.Count);

        var starters = new List<SuggestedPlacement>();

        for (var slot = 0; slot < slots.Length; slot++)
        {
            if (slotOwners[slot] < 0) continue;

            var player = players[slotOwners[slot]];
            starters.Add(new SuggestedPlacement
            {
                Player = player,
                SlotIndex = slot,
                Position = slots[slot],
                Fit = PositionFitHelper.GetFit(player, slots[slot])
            });
        }

        var placed = slotOwners.Where(owner => owner >= 0).ToHashSet();

        return new LineupSuggestion
        {
            Starters = starters,
            Substitutes = [.. players.Where((_, index) => !placed.Contains(index))]
        };
    }

    private static double Penalty(PositionFit fit) => fit switch
    {
        PositionFit.Preferred => 0,
        PositionFit.NaturalFit => 2,
        PositionFit.Alternative => 3,
        PositionFit.Compatible => 5,
        _ => 12
    };

    private static double[,] BuildCosts(
        PlayerPosition[] slots, List<Player> players, IReadOnlyDictionary<int, int> minutesPlayed)
    {
        // Against the busiest player rather than the spread, so that five minutes between two players early in a season does not weigh
        // as heavily as an hour between them in April.
        var busiest = players.Max(p => minutesPlayed.GetValueOrDefault(p.Id));

        var costs = new double[players.Count, slots.Length];

        for (var player = 0; player < players.Count; player++)
        {
            var share = busiest > 0
                ? (double)minutesPlayed.GetValueOrDefault(players[player].Id) / busiest
                : 0;

            for (var slot = 0; slot < slots.Length; slot++)
            {
                var fit = Penalty(PositionFitHelper.GetFit(players[player], slots[slot]));
                if (slots[slot] == PlayerPosition.GK) fit *= GoalkeeperMultiplier;

                costs[player, slot] = fit + share * RestWeight;
            }
        }

        return costs;
    }

    /// Slot index to player index, or -1 where there was nobody left to field.
    private static int[] Assign(double[,] costs, int slotCount, int playerCount)
    {
        var slotOwners = new int[slotCount];
        Array.Fill(slotOwners, -1);

        var placed = new bool[playerCount];

        for (var filled = 0; filled < Math.Min(slotCount, playerCount); filled++)
        {
            var bestPlayer = -1;
            var bestSlot = -1;
            var bestCost = double.MaxValue;

            for (var player = 0; player < playerCount; player++)
            {
                if (placed[player]) continue;

                for (var slot = 0; slot < slotCount; slot++)
                {
                    if (slotOwners[slot] >= 0 || costs[player, slot] >= bestCost) continue;

                    bestCost = costs[player, slot];
                    bestPlayer = player;
                    bestSlot = slot;
                }
            }

            slotOwners[bestSlot] = bestPlayer;
            placed[bestPlayer] = true;
        }

        Improve(costs, slotOwners, placed, playerCount);

        return slotOwners;
    }

    /// Taking the cheapest pair first can spend the only keeper on right back and leave nobody for goal. Trading assigned pairs and
    /// bringing players off the bench until neither helps is what turns the cheapest first choice into the cheapest whole line-up.
    private static void Improve(double[,] costs, int[] slotOwners, bool[] placed, int playerCount)
    {
        bool improved;

        do
        {
            improved = false;

            for (var slot = 0; slot < slotOwners.Length; slot++)
            {
                for (var other = slot + 1; other < slotOwners.Length; other++)
                {
                    var current = CostAt(costs, slotOwners[slot], slot) + CostAt(costs, slotOwners[other], other);
                    var swapped = CostAt(costs, slotOwners[other], slot) + CostAt(costs, slotOwners[slot], other);

                    if (swapped >= current - Tolerance) continue;

                    (slotOwners[slot], slotOwners[other]) = (slotOwners[other], slotOwners[slot]);
                    improved = true;
                }

                for (var player = 0; player < playerCount; player++)
                {
                    if (placed[player] || costs[player, slot] >= CostAt(costs, slotOwners[slot], slot) - Tolerance)
                        continue;

                    if (slotOwners[slot] >= 0) placed[slotOwners[slot]] = false;

                    slotOwners[slot] = player;
                    placed[player] = true;
                    improved = true;
                }
            }
        }
        while (improved);
    }

    /// An unfilled slot costs nothing, which is what lets a squad shorter than the formation be traded around like any other.
    private static double CostAt(double[,] costs, int playerIndex, int slotIndex) =>
        playerIndex < 0 ? 0 : costs[playerIndex, slotIndex];
}
