namespace FootballFormation.Core.Models;

/// <summary>
/// A score in venue order — home side first. Built by <see cref="Game.InVenueOrder"/> and
/// <see cref="Game.ScoreboardOrder"/>, the one place the flip from what <see cref="Game"/> actually
/// stores (always us/them) happens.
/// <para>
/// Compare <c>MatchScore</c> in <c>Core/Reporting/ScoreProgressionReport.cs</c> — same shape, but
/// always ours first; venue is a display concern that report has no use for.
/// </para>
/// </summary>
public readonly record struct VenueScore(int Home, int Away)
{
    public override string ToString() => $"{Home} – {Away}";
}
