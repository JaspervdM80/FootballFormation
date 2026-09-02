namespace FootballFormation.Core.Reporting;

public class PlayerTrainingAttendance
{
    public required Player Player { get; init; }

    /// Sessions that had already been held while she was a full member of that season's squad — never every session in the season, or a
    /// player who joined in January reads as having missed the autumn.
    public int Held { get; init; }

    public int Attended { get; init; }

    public int Missed => Held - Attended;

    /// Share of <see cref="Held"/> she was there for, 0–100.
    public double Percentage => Held > 0 ? Math.Round((double)Attended / Held * 100, 0) : 0;
}

/// Scope is whatever the caller passed to <see cref="TrainingAttendanceReport.Build"/>: one season, or every one of them.
public class TrainingAttendance
{
    /// Sessions that have been and gone. Ones still to come are not counted, and neither are <see cref="Cancelled"/> ones — those stay
    /// on file so the week reads honestly, but they are nobody's absence.
    public int Held { get; init; }

    public int Cancelled { get; init; }

    /// Best attendance first.
    public required List<PlayerTrainingAttendance> Players { get; init; }

    /// Share of every player-session that was attended, 0–100 — the squad's figure, not the average of the individual ones, so a player
    /// who was only there for the last month does not weigh the same as one who was there all year.
    public double Percentage
    {
        get
        {
            var held = Players.Sum(p => p.Held);
            return held > 0 ? Math.Round((double)Players.Sum(p => p.Attended) / held * 100, 0) : 0;
        }
    }

    // For a page whose load failed, so the markup needs no second shape to render.
    public static TrainingAttendance Empty { get; } = new() { Players = [] };
}

/// Who was expected comes from the squad rather than from the register: a session records who was *not* there, so attendance is squad
/// minus absent. Guests are left out — a training is the season's squad, and nobody else is expected (docs/models/training.md).
public static class TrainingAttendanceReport
{
    public static TrainingAttendance Build(IReadOnlyList<Training> trainings, SeasonSquads squads, DateTime today)
    {
        // A season's evenings are all written the day the training period is saved, so most of them are still ahead. One nobody could
        // have missed yet carries an empty absence list, which reads as a full squad present and pulls every figure up towards 100%.
        var past = trainings.Where(t => t.Date.Date < today.Date).ToList();
        var held = trainings.Where(t => t.HasBeenHeld(today)).ToList();

        var expected = held
            .SelectMany(t => squads.For(t.SeasonId).FullMembers)
            .DistinctBy(p => p.Id);

        return new TrainingAttendance
        {
            Held = held.Count,
            Cancelled = past.Count - held.Count,
            Players =
            [
                .. expected
                    .Select(p => BuildFor(p, held, squads, today))
                    .OrderByDescending(a => a.Percentage)
                    .ThenByDescending(a => a.Held)
                    .ThenBy(a => a.Player.DisplayName)
            ]
        };
    }

    /// Zero of zero for anyone in no squad of the scope — the player page is reachable for everyone on file, squad member or not.
    public static PlayerTrainingAttendance BuildFor(
        Player player, IEnumerable<Training> trainings, SeasonSquads squads, DateTime today)
    {
        var held = 0;
        var attended = 0;

        foreach (var training in trainings)
        {
            // An evening still ahead is nobody's yet and a cancelled one is nobody's absence; a guest was never expected at either.
            if (!training.HasBeenHeld(today)) continue;
            if (!squads.For(training.SeasonId).IsFullMember(player.Id)) continue;

            held++;
            if (!training.UnavailablePlayerIds.Contains(player.Id)) attended++;
        }

        return new PlayerTrainingAttendance { Player = player, Held = held, Attended = attended };
    }
}
