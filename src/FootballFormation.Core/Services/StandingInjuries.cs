using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Services;

/// <summary>
/// Writing down which squad members missed a match injured, at the moment the match becomes part of
/// the record.
/// <para>
/// <see cref="SeasonSquadMember.IsInjured"/> is a flag with no date on it: once it is cleared,
/// nothing is left to say which matches it kept her out of. So the flag is read exactly once per
/// match — on the transition to <see cref="Game.IsComplete"/> — and copied into
/// <see cref="Game.InjuredPlayerIds"/>, where it stops being a status and becomes history. Every
/// later read (<c>Game.IsInRoster</c>, <c>PlayerStatsReport</c>) goes to the game's own list, so
/// clearing the flag afterwards changes nothing that already happened.
/// </para>
/// </summary>
internal static class StandingInjuries
{
    /// <summary>
    /// Fills in <paramref name="game"/>'s record from the squad it is being played by. The caller
    /// saves — this is meant to ride the same <c>SaveChangesAsync</c> as whatever completed the
    /// match, so a match can never be settled without its absences beside it.
    /// </summary>
    internal static async Task RecordAsync(AppDbContext db, Game game, CancellationToken cancellationToken)
    {
        var injured = await db.SeasonSquadMembers
            .Where(m => m.SeasonId == game.SeasonId && m.IsInjured && !m.IsGuest)
            .Select(m => m.PlayerId)
            .ToListAsync(cancellationToken);

        if (injured.Count == 0) return;

        // Flagged but on the pitch anyway — hurt after the final whistle, or flagged in the same
        // evening the score was typed in. The lineup is the better witness of who actually missed it.
        var played = await db.GamePlayerPositions
            .Where(p => p.GamePeriod.GameId == game.Id && injured.Contains(p.PlayerId))
            .Select(p => p.PlayerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        game.InjuredPlayerIds = [.. injured.Except(played).Order()];
    }
}
