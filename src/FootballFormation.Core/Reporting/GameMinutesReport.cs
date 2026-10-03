namespace FootballFormation.Core.Reporting;

public class GameMinutes
{
    /// Player id → position → seconds spent on the pitch in that position.
    public required IReadOnlyDictionary<int, IReadOnlyDictionary<PlayerPosition, int>> SecondsByPlayer { get; init; }

    /// Everyone named in a line-up or a substitution, players with zero seconds included.
    public required IReadOnlySet<int> PlayerIds { get; init; }

    /// Empty unless a half is being played right now.
    public required IReadOnlySet<int> OnPitchNow { get; init; }

    /// False when these are the planned periods × period length estimate rather than real timings, which callers have to label.
    public required bool IsActual { get; init; }

    public IReadOnlyDictionary<PlayerPosition, int> PositionsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions)
            ? positions
            : new Dictionary<PlayerPosition, int>();

    public int SecondsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions) ? positions.Values.Sum() : 0;
}

/// Decides per game on <see cref="Game.HasActualTimings"/> whether minutes come from what happened or what was planned — crediting an
/// un-kicked-off line-up inside a played half would invent playing time. See docs/known_issues/domain.md for the position-split limit.
public static class GameMinutesReport
{
    /// <paramref name="elapsedSeconds"/> closes off the running half, so any value will do for a settled game.
    public static GameMinutes Build(Game game, int elapsedSeconds = 0)
    {
        var seconds = new Dictionary<int, Dictionary<PlayerPosition, int>>();
        var known = new HashSet<int>();
        var onPitchNow = new HashSet<int>();
        var isActual = game.HasActualTimings;

        foreach (var period in game.Periods.OrderBy(p => p.PeriodType))
        {
            foreach (var entry in period.PlayerPositions) known.Add(entry.PlayerId);

            if (!isActual)
            {
                // No timings anywhere in this game, so everyone fielded gets the whole period and substitutes get nothing.
                foreach (var entry in period.PlayerPositions.Where(p => !p.IsSubstitute))
                    Credit(seconds, entry.PlayerId, entry.Position, game.PeriodDurationSeconds);

                continue;
            }

            // A line-up never kicked off contributes no time — it is a mid-half plan, and the half's own line-up already accounts for it.
            if (period.StartedAtSeconds is not { } start) continue;

            var isLive = game.LivePeriodId == period.Id;
            var end = period.EndedAtSeconds ?? (isLive ? elapsedSeconds : start);

            var walk = HalfLineupWalk.Rewind(game, period).Walk(start, end);

            foreach (var stretch in walk.Stretches)
            {
                foreach (var (playerId, spot) in stretch.OnPitch)
                {
                    known.Add(playerId);
                    Credit(seconds, playerId, spot.Position, stretch.ToSeconds - stretch.FromSeconds);
                }
            }

            if (isLive) onPitchNow = [.. walk.AtWhistle.Keys];
        }

        return new GameMinutes
        {
            SecondsByPlayer = seconds.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<PlayerPosition, int>)kv.Value),
            PlayerIds = known,
            OnPitchNow = onPitchNow,
            IsActual = isActual
        };
    }

    private static void Credit(
        Dictionary<int, Dictionary<PlayerPosition, int>> seconds,
        int playerId,
        PlayerPosition position,
        int span)
    {
        if (span <= 0) return;

        if (!seconds.TryGetValue(playerId, out var positions))
            seconds[playerId] = positions = [];

        positions[position] = positions.GetValueOrDefault(position) + span;
    }
}
