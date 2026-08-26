namespace FootballFormation.Core.Reporting;

/// A scoreboard reading, not the time the game has been running for: <paramref name="Seconds"/> is capped at the end of the half and the
/// overrun counted separately in <paramref name="AdditionalSeconds"/>.
public record MatchClock(int Seconds, int AdditionalSeconds, MatchMinute Minute)
{
    public static readonly MatchClock BeforeKickOff = new(0, 0, new MatchMinute(1, 0));

    public bool IsInAdditionalTime => AdditionalSeconds > 0;
}

/// Presentation only — what is stored stays the real elapsed time, so playing time and season statistics are unaffected. The second half
/// starts at half the match duration whatever the first half cost, so an over-running first half does not push the rest out of step.
public static class MatchClockReport
{
    /// A null <paramref name="displayHalf"/> means there is nothing to show yet; otherwise it decides where the clock starts and stops.
    public static MatchClock Build(Game game, GamePeriod? displayHalf, int elapsedSeconds)
    {
        if (displayHalf is null) return MatchClock.BeforeKickOff;

        // Always halves, whatever the line-ups were planned in — a scoreboard counts in halves even for a quarters game.
        var halfSeconds = GameSplitType.Halves.PeriodDurationSeconds(game.GameDurationMinutes);

        var half = displayHalf.PeriodType.Half();
        var plannedStart = half == PeriodType.FirstHalf ? 0 : halfSeconds;

        if (HalfKickedOffAt(game, half) is not { } actualStart)
            return new MatchClock(plannedStart, 0, PlainMinute(plannedStart));

        var intoHalf = Math.Max(0, elapsedSeconds - actualStart);

        // A game with no duration on file has nothing to cap against; showing the time as it runs
        // beats reporting the whole half as additional time.
        if (halfSeconds <= 0) return new MatchClock(intoHalf, 0, PlainMinute(intoHalf));

        // The minute stands still with the capped clock, so stoppage time reads 35+1, 35+2 — which is what an event there is filed under.
        if (intoHalf >= halfSeconds)
        {
            return new MatchClock(
                plannedStart + halfSeconds,
                intoHalf - halfSeconds,
                new MatchMinute((plannedStart + halfSeconds) / 60, ((intoHalf - halfSeconds) / 60) + 1));
        }

        return new MatchClock(plannedStart + intoHalf, 0, PlainMinute(plannedStart + intoHalf));
    }

    public static MatchMinute MinuteOf(Game game, GameSubstitution substitution) =>
        MinuteAt(game, substitution.GamePeriodId, substitution.AtSeconds);

    public static MatchMinute MinuteOf(Game game, GameInjury injury) =>
        MinuteAt(game, injury.GamePeriodId, injury.AtSeconds);

    /// Derived from the half timings rather than stored, so correcting a half corrects its goals with it. Null when a goal has neither a
    /// clock reading nor a typed minute, which the result page allows.
    public static MatchMinute? MinuteOf(Game game, GameGoal goal) => goal switch
    {
        { AtSeconds: { } at } => MinuteAt(game, goal.GamePeriodId, at),
        { Minute: { } minute } => new MatchMinute(minute, 0),
        _ => null
    };

    /// A typed-in minute is a scoreboard reading, and the two scales part company once a half over-runs: on a match whose first half ran
    /// to 33, the scoreboard's 32' is 34 minutes elapsed. Converting it back here is <see cref="Build"/>'s arithmetic run the other way.
    public static int ElapsedOf(Game game, GameGoal goal)
    {
        if (goal.AtSeconds is { } at) return at;
        if (goal.Minute is not { } minute) return 0;

        // The start of the minute written down, on the scoreboard's scale.
        var onScoreboard = Math.Max(0, (minute - 1) * 60);

        var halfSeconds = GameSplitType.Halves.PeriodDurationSeconds(game.GameDurationMinutes);
        if (halfSeconds <= 0) return onScoreboard;

        // The fallbacks match what Build assumes in the same position, so a match never run from the touchline keeps its goals in the
        // order the typed minutes put them in — the only order they have.
        return onScoreboard < halfSeconds
            ? (HalfKickedOffAt(game, PeriodType.FirstHalf) ?? 0) + onScoreboard
            : (HalfKickedOffAt(game, PeriodType.SecondHalf) ?? halfSeconds) + (onScoreboard - halfSeconds);
    }

    /// What puts the half-time break on the timeline. Falls back to the first half when nothing says otherwise: a match never run from
    /// the touchline has no kick-off to be past, and one unbroken list is the honest way to show it.
    public static PeriodType HalfOf(Game game, int? periodId, int atSeconds) =>
        FindPeriod(game, periodId) is { } period ? period.PeriodType.Half()
            : HalfKickedOffAt(game, PeriodType.SecondHalf) is { } restart && atSeconds >= restart
                ? PeriodType.SecondHalf
                : PeriodType.FirstHalf;

    /// Falls back to the raw minute when the half was not loaded — a wrong-looking minute beats claiming 1'.
    private static MatchMinute MinuteAt(Game game, int? periodId, int atSeconds) =>
        FindPeriod(game, periodId) is { } half
            ? Build(game, half, atSeconds).Minute
            : PlainMinute(atSeconds);

    private static GamePeriod? FindPeriod(Game game, int? periodId) =>
        periodId is { } id ? game.Periods.FirstOrDefault(p => p.Id == id) : null;

    /// The first minute of play is 1', not 0'.
    private static MatchMinute PlainMinute(int seconds) => new((seconds / 60) + 1, 0);

    /// The earliest of the half's line-ups to start: a quarters game plans two per half, and only the first of them opens it.
    private static int? HalfKickedOffAt(Game game, PeriodType half)
    {
        int? earliest = null;

        foreach (var period in game.Periods)
        {
            if (period.PeriodType.Half() != half || period.StartedAtSeconds is not { } start) continue;
            if (earliest is null || start < earliest) earliest = start;
        }

        return earliest;
    }
}
