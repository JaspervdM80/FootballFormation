namespace FootballFormation.Core.Models;

/// Home side first, which <see cref="Game"/> never stores — <see cref="Game.InVenueOrder"/> is the one place that flip happens. Compare
/// MatchScore, the same shape kept always-ours-first because venue is a display concern its report has no use for.
public readonly record struct VenueScore(int Home, int Away)
{
    public override string ToString() => $"{Home} – {Away}";
}
