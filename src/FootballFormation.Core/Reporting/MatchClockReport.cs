using FootballFormation.Core.Models;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// The match clock as a scoreboard shows it, which is not the same as the time the game has been
/// running for.
/// </summary>
/// <param name="Seconds">The reading on the clock, capped at the end of the half being played.</param>
/// <param name="AdditionalSeconds">How far past the end of the half play has gone, counted separately.</param>
public record MatchClock(int Seconds, int AdditionalSeconds)
{
    public static readonly MatchClock BeforeKickOff = new(0, 0);

    public bool IsInAdditionalTime => AdditionalSeconds > 0;

    /// <summary>
    /// The clock running on past the cap — the number a goal is written down against. Football
    /// counts a stoppage-time goal into the following minutes rather than pinning it to the cap,
    /// and several goals in stoppage time must not all land on the same minute.
    /// </summary>
    public int TotalSeconds => Seconds + AdditionalSeconds;

    /// <summary>The minute this instant falls in. The first minute of play is 1', as scorelines are written.</summary>
    public int Minute => (TotalSeconds / 60) + 1;
}

/// <summary>
/// Turns the raw match clock into the reading a scoreboard shows. Two things separate them:
/// <list type="bullet">
/// <item>A half is only ever as long as a half. Once it is played out the clock stops at the end
/// of the half and the overrun is reported as additional time.</item>
/// <item>The second half starts at half the match duration whatever the first half actually cost,
/// so an over-running first half does not push the whole second half out of step.</item>
/// </list>
/// This is presentation only. What is stored stays the real elapsed time, so playing time,
/// substitution timings and the season statistics are unaffected.
/// </summary>
public static class MatchClockReport
{
    /// <param name="displayPeriod">The period on screen; its half decides where the clock starts
    /// and where it stops. Null before there is anything to show.</param>
    /// <param name="elapsedSeconds">The real match clock right now.</param>
    public static MatchClock Build(Game game, GamePeriod? displayPeriod, int elapsedSeconds)
    {
        if (displayPeriod is null) return MatchClock.BeforeKickOff;

        // Always halves, whatever the game is split into — a quarters game is still two halves,
        // and the scoreboard counts in halves.
        var halfSeconds = GameSplitType.Halves.PeriodDurationSeconds(game.GameDurationMinutes);

        var half = displayPeriod.PeriodType.Half();
        var plannedStart = half == PeriodType.FirstHalf ? 0 : halfSeconds;

        if (HalfKickedOffAt(game, half) is not { } actualStart)
            return new MatchClock(plannedStart, 0);

        var intoHalf = Math.Max(0, elapsedSeconds - actualStart);

        // A game with no duration on file has nothing to cap against; showing the time as it runs
        // beats reporting the whole half as additional time.
        if (halfSeconds <= 0) return new MatchClock(intoHalf, 0);

        return new MatchClock(
            plannedStart + Math.Min(intoHalf, halfSeconds),
            Math.Max(0, intoHalf - halfSeconds));
    }

    /// <summary>
    /// The real clock reading when this half kicked off — the earliest of its periods to start.
    /// A quarters game has two periods per half and only the first of them opens the half.
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
