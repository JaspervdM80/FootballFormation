namespace FootballFormation.Core.Services;

/// The training counterpart of <see cref="StandingInjuries"/>. A match settles at the moment it is played, so it can copy the undated
/// flag as it stands; a session has no such moment, so it is stamped the first time anything reads it after the evening has passed,
/// and <see cref="SeasonSquadMember.InjuredSince"/> is what stops today's injury reaching back into September.
internal static class StandingTrainingInjuries
{
    internal static async Task SettleAsync(
        AppDbContext db, int? seasonId, DateTime today, CancellationToken cancellationToken)
    {
        // HasBeenHeld in memory, never in SQL — see QueryTags.ComparesDatesInSql.
        var pending = (await db.Trainings
            .Where(t => (seasonId == null || t.SeasonId == seasonId) && !t.AbsencesRecorded && !t.DidNotTakePlace)
            .ToListAsync(cancellationToken))
            .Where(t => t.HasBeenHeld(today))
            .ToList();

        if (pending.Count == 0) return;

        var seasonIds = pending.Select(t => t.SeasonId).Distinct().ToList();
        var injured = await db.SeasonSquadMembers
            .Where(m => seasonIds.Contains(m.SeasonId) && m.IsInjured && !m.IsGuest)
            .Select(m => new { m.SeasonId, m.PlayerId, m.InjuredSince })
            .ToListAsync(cancellationToken);

        foreach (var training in pending)
        {
            training.AbsencesRecorded = true;

            // Already named absent by the coach, whatever the reason: her register is the better witness, and counting her in both
            // lists would count her twice.
            training.InjuredPlayerIds =
            [
                .. injured
                    .Where(m => m.SeasonId == training.SeasonId
                                && m.InjuredSince is not null && m.InjuredSince.Value.Date <= training.Date.Date
                                && !training.UnavailablePlayerIds.Contains(m.PlayerId))
                    .Select(m => m.PlayerId)
                    .Order()
            ];
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
