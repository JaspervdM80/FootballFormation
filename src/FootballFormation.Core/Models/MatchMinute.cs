namespace FootballFormation.Core.Models;

/// <summary>
/// The minute an event is written down against, the way football writes one: 35, or 35+2 once the
/// half has been played out and the clock is into stoppage time.
/// <para>
/// The pair is also what puts events in order, which a single number cannot do. A goal in first-half
/// stoppage time and one just after the restart are barely a minute apart, but a clock that counted
/// straight on would write the first as 37 and the second as 36 and list them the wrong way round.
/// Comparing on <paramref name="Minute"/> first and <paramref name="Additional"/> second is
/// chronological across the whole match, because a half's clock stops at the half and the half that
/// follows starts above it.
/// </para>
/// </summary>
/// <param name="Minute">The minute on the clock, never past the end of the half being played.</param>
/// <param name="Additional">Minutes into stoppage time, counted from 1; zero during normal play.</param>
public readonly record struct MatchMinute(int Minute, int Additional) : IComparable<MatchMinute>
{
    public bool IsAdditional => Additional > 0;

    public int CompareTo(MatchMinute other) => Minute != other.Minute
        ? Minute.CompareTo(other.Minute)
        : Additional.CompareTo(other.Additional);

    public override string ToString() => IsAdditional ? $"{Minute}+{Additional}" : $"{Minute}";
}
