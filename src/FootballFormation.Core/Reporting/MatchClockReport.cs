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
/// <see cref="MatchMinute"/> — 35+2 rather than a 37 that would sort after the restart.</item>
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
        // shows, and the only way several stoppage-time events keep their order.
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
    /// made, which is the half's reading and not the raw elapsed time. Falls back to the raw minute
    /// for a substitution whose half was not loaded — a wrong-looking minute beats claiming 1'.
    /// </summary>
    public static MatchMinute MinuteOf(Game game, GameSubstitution substitution) =>
        game.Periods.FirstOrDefault(p => p.Id == substitution.GamePeriodId) is { } half
            ? Build(game, half, substitution.AtSeconds).Minute
            : PlainMinute(substitution.AtSeconds);

    /// <summary>
    /// The minute a goal was written down against, or null for one recorded without a minute —
    /// which the result page allows and the timeline then has nothing to place.
    /// </summary>
    public static MatchMinute? MinuteOf(GameGoal goal) =>
        goal.Minute is { } minute ? new MatchMinute(minute, goal.AdditionalMinute) : null;

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
