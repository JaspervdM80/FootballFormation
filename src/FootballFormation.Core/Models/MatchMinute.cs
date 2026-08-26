namespace FootballFormation.Core.Models;

/// The minute an event is written down against, the way football writes one: 35, or 35+2 in stoppage time. Display only — ordering a
/// timeline is the elapsed clock's job, which runs on across the break and needs no pair to get 35+2 before the second half's 36th.
public readonly record struct MatchMinute(int Minute, int Additional)
{
    public bool IsAdditional => Additional > 0;

    public override string ToString() => IsAdditional ? $"{Minute}+{Additional}" : $"{Minute}";
}
