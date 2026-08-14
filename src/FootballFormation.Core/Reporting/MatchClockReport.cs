using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// The match clock as a scoreboard shows it, which is not the same as the time the game has been
/// running for.
/// </summary>
/// <param name="Seconds">The reading on the clock, capped at the end of the half being played.</param>
/// <param name="AdditionalSeconds">How far past the end of the half play has gone, counted separately.</param>
/// <param name="Minute">The minute an event at this instant is written down against.</param>
public record MatchClock(int Seconds, int AdditionalSeconds, MatchMinute Minute)
{
    public static readonly MatchClock BeforeKickOff = new(0, 0, new MatchMinute(1, 0));

    public bool IsInAdditionalTime => AdditionalSeconds > 0;
}

/// <summary>
/// Turns the raw match clock into the reading a scoreboard shows. Two things separate them:
/// <list type="bullet">
/// <item>A half is only ever as long as a half. Once it is played out the clock stops at the end
/// of the half and the overrun is reported as additional time.</item>
/// <item>The second half starts at half the match duration whatever the first half actually cost,
/// so an over-running first half does not push the whole second half out of step.</item>
/// <item>The minute an event is written down against follows that same reading, as a
/// <see cref="MatchMinute"/> — 35+2 rather than a 37 nobody at the pitch would recognise.</item>
/// </list>
/// This is presentation only. What is stored stays the real elapsed time, so playing time,
/// substitution timings and the season statistics are unaffected.
/// </summary>
public static class MatchClockReport
{
    /// <param name="displayHalf">The half on screen, as the line-up it is played with. It decides
    /// where the clock starts and where it stops. Null before there is anything to show.</param>
    /// <param name="elapsedSeconds">The real match clock right now.</param>
    public static MatchClock Build(Game game, GamePeriod? displayHalf, int elapsedSeconds)
    {
        if (displayHalf is null) return MatchClock.BeforeKickOff;

        // Always halves, whatever the line-ups were planned in — a quarters game is still two
        // halves, and the scoreboard counts in halves.
        var halfSeconds = GameSplitType.Halves.PeriodDurationSeconds(game.GameDurationMinutes);

        var half = displayHalf.PeriodType.Half();
        var plannedStart = half == PeriodType.FirstHalf ? 0 : halfSeconds;

        if (HalfKickedOffAt(game, half) is not { } actualStart)
            return new MatchClock(plannedStart, 0, PlainMinute(plannedStart));

        var intoHalf = Math.Max(0, elapsedSeconds - actualStart);

        // A game with no duration on file has nothing to cap against; showing the time as it runs
        // beats reporting the whole half as additional time.
        if (halfSeconds <= 0) return new MatchClock(intoHalf, 0, PlainMinute(intoHalf));

        // Once the half is played out the clock stands still at the cap, so the minute stands still
        // with it and the overrun is counted alongside as 35+1, 35+2 — the reading a scoreboard
        // shows, and what an event in stoppage time is written down against.
        if (intoHalf >= halfSeconds)
        {
            return new MatchClock(
                plannedStart + halfSeconds,
                intoHalf - halfSeconds,
                new MatchMinute((plannedStart + halfSeconds) / 60, ((intoHalf - halfSeconds) / 60) + 1));
        }

        return new MatchClock(plannedStart + intoHalf, 0, PlainMinute(plannedStart + intoHalf));
    }

    /// <summary>
    /// The minute a substitution is written down against: the reading the clock showed when it was
    /// made, which is the half's reading and not the raw elapsed time.
    /// </summary>
    public static MatchMinute MinuteOf(Game game, GameSubstitution substitution) =>
        MinuteAt(game, substitution.GamePeriodId, substitution.AtSeconds);

    /// <summary>
    /// The minute a goal is written down against — derived the same way a substitution's is, from
    /// the clock reading and the half it was scored in, so correcting a half's timings corrects
    /// its goals with it. A goal with no clock behind it falls back to the minute stored on the
    /// row, and one with neither has no minute at all, which the result page allows.
    /// </summary>
    public static MatchMinute? MinuteOf(Game game, GameGoal goal) => goal switch
    {
        { AtSeconds: { } at } => MinuteAt(game, goal.GamePeriodId, at),
        { Minute: { } minute } => new MatchMinute(minute, 0),
        _ => null
    };

    /// <summary>
    /// Where a goal sits on the elapsed match clock — the scale the timeline is ordered on, and
    /// the one a substitution is already stored in.
    /// <para>
    /// A goal logged from the touchline has that reading on the row. A goal typed in on the result
    /// page has only the minute somebody wrote, which is a <em>scoreboard</em> reading, and the two
    /// scales part company the moment a half over-runs: on a 60-minute match whose first half ran
    /// to 33, the scoreboard's 32' is 34 minutes of elapsed play. Reading the typed minute as
    /// though it were elapsed time filed it before the restart, under the half-time rule and ahead
    /// of goals that were really scored first, so it is converted back through the half timings
    /// here — the same arithmetic <see cref="Build"/> does, run the other way.
    /// </para>
    /// </summary>
    public static int ElapsedOf(Game game, GameGoal goal)
    {
        if (goal.AtSeconds is { } at) return at;
        if (goal.Minute is not { } minute) return 0;

        // The start of the minute written down, on the scoreboard's scale.
        var onScoreboard = Math.Max(0, (minute - 1) * 60);

        var halfSeconds = GameSplitType.Halves.PeriodDurationSeconds(game.GameDurationMinutes);
        if (halfSeconds <= 0) return onScoreboard;

        // A match never run from the touchline has no timings to convert through, and the fallbacks
        // are what Build assumes in the same position — so its goals keep the order the typed
        // minutes put them in, which is the only order they have.
        return onScoreboard < halfSeconds
            ? (HalfKickedOffAt(game, PeriodType.FirstHalf) ?? 0) + onScoreboard
            : (HalfKickedOffAt(game, PeriodType.SecondHalf) ?? halfSeconds) + (onScoreboard - halfSeconds);
    }

    /// <summary>
    /// The half an event belongs to, which is what puts the half-time break on the timeline. Its
    /// own line-up's half when it has one; otherwise whichever side of the second half's kick-off
    /// its elapsed reading falls, which is all a goal typed in by hand leaves to go on. First half
    /// when nothing says otherwise — a match never run from the touchline has no kick-off to be
    /// past, and one unbroken list is the honest way to show it.
    /// </summary>
    public static PeriodType HalfOf(Game game, int? periodId, int atSeconds) =>
        FindPeriod(game, periodId) is { } period ? period.PeriodType.Half()
            : HalfKickedOffAt(game, PeriodType.SecondHalf) is { } restart && atSeconds >= restart
                ? PeriodType.SecondHalf
                : PeriodType.FirstHalf;

    /// <summary>
    /// The reading the clock showed at a moment in a given half. Falls back to the raw minute when
    /// that half was not loaded — a wrong-looking minute beats claiming 1'.
    /// </summary>
    private static MatchMinute MinuteAt(Game game, int? periodId, int atSeconds) =>
        FindPeriod(game, periodId) is { } half
            ? Build(game, half, atSeconds).Minute
            : PlainMinute(atSeconds);

    private static GamePeriod? FindPeriod(Game game, int? periodId) =>
        periodId is { } id ? game.Periods.FirstOrDefault(p => p.Id == id) : null;

    /// <summary>The minute a plain clock reading falls in. The first minute of play is 1'.</summary>
    private static MatchMinute PlainMinute(int seconds) => new((seconds / 60) + 1, 0);

    /// <summary>
    /// The real clock reading when this half kicked off — the earliest of its line-ups to start.
    /// A quarters game plans two line-ups per half and only the first of them opens the half.
    /// </summary>
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
