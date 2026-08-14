namespace FootballFormation.Core.Models;

/// <summary>
/// The minute an event is written down against, the way football writes one: 35, or 35+2 once the
/// half has been played out and the clock is into stoppage time.
/// <para>
/// Display only. Ordering a timeline is the elapsed clock's job — <c>MatchClockReport.ElapsedOf</c>
/// and <see cref="GameSubstitution.AtSeconds"/> — which runs on across the break and needs no pair
/// to say that a goal at 35+2 came before one in the 36th minute of the second half.
/// </para>
/// </summary>
/// <param name="Minute">The minute on the clock, never past the end of the half being played.</param>
/// <param name="Additional">Minutes into stoppage time, counted from 1; zero during normal play.</param>
public readonly record struct MatchMinute(int Minute, int Additional)
{
    public bool IsAdditional => Additional > 0;

    public override string ToString() => IsAdditional ? $"{Minute}+{Additional}" : $"{Minute}";
}
