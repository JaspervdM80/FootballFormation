namespace FootballFormation.Core.Models;

/// <summary>
/// A pair of numbers in venue order — the home side first, the away side second. The one flip
/// between what <see cref="Game"/> actually stores (always us/them, regardless of venue — see
/// <see cref="Game.ScoreHome"/>) and how a scoreboard reads; built by <see cref="Game.InVenueOrder"/>
/// and <see cref="Game.ScoreboardOrder"/> so the flip is spelled out once rather than wherever a
/// scoreline is shown.
/// <para>
/// Compare <c>MatchScore</c> in <c>Core/Reporting/ScoreProgressionReport.cs</c> — the same shape,
/// but always ours first. That one stays in us/them order on purpose; venue is a display concern,
/// and the live timeline it feeds has no venue of its own to flip by.
/// </para>
/// </summary>
public readonly record struct VenueScore(int Home, int Away)
{
    public override string ToString() => $"{Home} – {Away}";
}
