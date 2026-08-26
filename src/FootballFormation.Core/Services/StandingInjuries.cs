namespace FootballFormation.Core.Services;

/// <see cref="SeasonSquadMember.IsInjured"/> is a flag with no date on it, so it is read exactly once per match — as the match settles —
/// and copied into <see cref="Game.InjuredPlayerIds"/>, where it stops being a status and becomes history that clearing cannot rewrite.
internal static class StandingInjuries
{
    /// Answers only the first time a game settles (<see cref="Game.AbsencesRecorded"/>), so callers can ask on every completion. The
    /// caller saves: this must ride the same SaveChangesAsync as whatever settled the match.
    internal static async Task RecordAsync(AppDbContext db, Game game, CancellationToken cancellationToken)
    {
        if (game.AbsencesRecorded) return;
        game.AbsencesRecorded = true;

        var injured = await db.SeasonSquadMembers
            .Where(m => m.SeasonId == game.SeasonId && m.IsInjured && !m.IsGuest)
            .Select(m => m.PlayerId)
            .ToListAsync(cancellationToken);

        if (injured.Count == 0) return;

        // Flagged but on the pitch anyway — hurt after the whistle, or flagged the same evening the score was typed. The line-up is the
        // better witness of who actually missed it.
        var played = await db.GamePlayerPositions
            .Where(p => p.GamePeriod.GameId == game.Id && injured.Contains(p.PlayerId))
            .Select(p => p.PlayerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        game.InjuredPlayerIds = [.. injured.Except(played).Order()];
    }
}
