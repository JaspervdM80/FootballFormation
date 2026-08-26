namespace FootballFormation.Core.Reporting;

public class GameMinutes
{
    /// <summary>Player id → position → seconds spent on the pitch in that position.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyDictionary<PlayerPosition, int>> SecondsByPlayer { get; init; }

    /// <summary>Everyone named in a lineup or a substitution, including players with zero seconds.</summary>
    public required IReadOnlySet<int> PlayerIds { get; init; }

    /// <summary>Who is on the pitch right now. Only populated while a half is being played.</summary>
    public required IReadOnlySet<int> OnPitchNow { get; init; }

    /// <summary>
    /// False when the figures are the planned <c>periods × period length</c> estimate rather than
    /// real timings, so callers can label them as such.
    /// </summary>
    public required bool IsActual { get; init; }

    public IReadOnlyDictionary<PlayerPosition, int> PositionsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions)
            ? positions
            : new Dictionary<PlayerPosition, int>();

    public int SecondsFor(int playerId) =>
        SecondsByPlayer.TryGetValue(playerId, out var positions) ? positions.Values.Sum() : 0;
}

/// <summary>
/// Playing time for one game, per player and per position. The single place that decides whether
/// a game's minutes come from what actually happened or from what was planned.
/// <para>
/// A game that was run live carries the truth in its half timings, its
/// <see cref="GameSubstitution"/> rows and any <see cref="GameInjury"/> nobody came on for. The
/// lineup alone cannot express it — <c>MatchSubstitutionService</c> rewrites it in place, so
/// afterwards it shows only the <em>final</em> occupants.
/// </para>
/// <para>
/// The choice is made per game, not per line-up, on <see cref="Game.HasActualTimings"/>: once a
/// match has been run live, a line-up with no kick-off is one the coach worked towards by hand
/// inside a half that is already accounted for, and crediting it a full period's minutes would
/// invent playing time.
/// </para>
/// <para>
/// Known limitation: only a substitution records a position change. The walk below starts from the
/// lineup as it <em>finally</em> stands and rewinds substitution rows, so a player who shifts
/// position mid-half without one is credited the position they ended in for the whole half,
/// the minutes before the shift included. The live screen's position swap
/// (<c>MatchSubstitutionService.SwapPositionsAsync</c>) is exactly that case: it rewrites the
/// lineup and writes nothing down, because a <see cref="GameSubstitution"/> would say someone left
/// the pitch. Totals stay right; only the split by position does. That is a gap in what gets
/// recorded, not in this calculation.
/// </para>
/// </summary>
public static class GameMinutesReport
{
    /// <param name="elapsedSeconds">The match clock right now, which closes off the running half.
    /// Irrelevant for a settled game — any value will do.</param>
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
                // No timings anywhere in this game: everyone fielded gets the whole period in the
                // position they were planned for. Substitutes get nothing, as before.
                foreach (var entry in period.PlayerPositions.Where(p => !p.IsSubstitute))
                    Credit(seconds, entry.PlayerId, entry.Position, game.PeriodDurationSeconds);

                continue;
            }

            // A line-up that was never kicked off contributes no time — it is a plan for the
            // middle of a half whose minutes the half's own line-up already accounts for.
            if (period.StartedAtSeconds is not { } start) continue;

            var isLive = game.LivePeriodId == period.Id;
            var end = period.EndedAtSeconds ?? (isLive ? elapsedSeconds : start);

            var changes = ChangesIn(game, period);

            // The lineup records where everyone stands *now*. Rewinding this half's changes
            // recovers who stood where when it kicked off, which is the only point the forward
            // walk below can start from. Each change carries the position that changed hands, so
            // it hands the slot back to the player who left it.
            var onPitch = period.PlayerPositions
                .Where(p => !p.IsSubstitute)
                .ToDictionary(p => p.PlayerId, p => p.Position);

            for (var i = changes.Count - 1; i >= 0; i--)
            {
                if (changes[i].PlayerOnId is { } cameOn) onPitch.Remove(cameOn);
                onPitch[changes[i].PlayerOffId] = changes[i].Position;
            }

            var cursor = start;
            foreach (var change in changes)
            {
                CreditAll(seconds, onPitch, change.AtSeconds - cursor);
                onPitch.Remove(change.PlayerOffId);

                if (change.PlayerOnId is { } cameOn)
                {
                    onPitch[cameOn] = change.Position;
                    known.Add(cameOn);
                }

                cursor = change.AtSeconds;
            }

            CreditAll(seconds, onPitch, end - cursor);

            if (isLive) onPitchNow = [.. onPitch.Keys];
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

    /// <summary>
    /// One player leaving the pitch, and the one who took the place over when somebody did. A
    /// substitution is both halves of that; an injury with nobody coming on is only the first.
    /// </summary>
    private readonly record struct LineupChange(
        int AtSeconds, int Id, int PlayerOffId, int? PlayerOnId, PlayerPosition Position);

    /// <summary>
    /// Everything that moved somebody off the pitch in this line-up, in the order it happened.
    /// <para>
    /// An injury a substitution already accounts for is left out: one touchline action writes both
    /// rows, and walking the pair would take the same player off twice — handing her slot back in
    /// the rewind above. Two changes in the same second are routine (a double substitution is two
    /// taps), and the walk only reaches the right kick-off line-up if it takes them in the order
    /// they were made, which is what the id settles — <c>RecordedAt</c> can be the same instant too.
    /// </para>
    /// </summary>
    private static List<LineupChange> ChangesIn(Game game, GamePeriod period)
    {
        var subs = game.Substitutions
            .Where(s => s.GamePeriodId == period.Id)
            .Select(s => new LineupChange(s.AtSeconds, s.Id, s.PlayerOffId, s.PlayerOnId, s.Position));

        var unreplaced = game.Injuries
            .Where(i => i.GamePeriodId == period.Id && !game.WasReplaced(i))
            .Select(i => new LineupChange(i.AtSeconds, i.Id, i.PlayerId, null, i.Position));

        return [.. subs.Concat(unreplaced).OrderBy(c => c.AtSeconds).ThenBy(c => c.Id)];
    }

    /// <summary>
    /// Adds a stretch of time to everyone on the pitch, each in the position they held for it.
    /// Non-positive spans are ignored — two substitutions in the same second are normal and must
    /// not subtract time.
    /// </summary>
    private static void CreditAll(
        Dictionary<int, Dictionary<PlayerPosition, int>> seconds,
        Dictionary<int, PlayerPosition> onPitch,
        int span)
    {
        if (span <= 0) return;

        foreach (var (playerId, position) in onPitch)
            Credit(seconds, playerId, position, span);
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
