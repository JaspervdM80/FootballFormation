namespace FootballFormation.Core.Tests;

/// Attendance is the squad minus the absentees: a session records who was *not* there, so who was expected has to come from somewhere
/// else. These pin which sessions count and for whom.
public class TrainingAttendanceReportTests
{
    private static readonly Player Ann = TestData.Player(1, "Ann", shirt: 7);
    private static readonly Player Bea = TestData.Player(2, "Bea", shirt: 9);

    private static Training Session(int id, int seasonId = 1, bool cancelled = false, params int[] absent) =>
        new()
        {
            Id = id,
            SeasonId = seasonId,
            Date = new DateTime(2026, 3, id),
            DidNotTakePlace = cancelled,
            UnavailablePlayerIds = [.. absent]
        };

    private static SeasonSquads Squads(int seasonId, IEnumerable<Player> players, int[]? guestIds = null) =>
        SeasonSquads.Of(TestData.Squad(seasonId, players, guestIds));

    [Fact]
    public void Everyone_the_session_did_not_record_as_absent_was_there()
    {
        var attendance = TrainingAttendanceReport.Build(
            [Session(1), Session(2, absent: Ann.Id), Session(3)],
            Squads(1, [Ann, Bea]));

        var ann = attendance.Players.Single(p => p.Player.Id == Ann.Id);

        Assert.Equal(3, ann.Held);
        Assert.Equal(2, ann.Attended);
        Assert.Equal(1, ann.Missed);
        Assert.Equal(67, ann.Percentage);
        Assert.Equal(100, attendance.Players.Single(p => p.Player.Id == Bea.Id).Percentage);
    }

    [Fact]
    public void A_session_that_did_not_take_place_is_nobodys_absence()
    {
        // The flag is the only thing telling a cancelled evening from one everybody happened to attend — both carry an empty
        // absence list, so counting it would quietly punish the squad for an evening that never happened.
        var attendance = TrainingAttendanceReport.Build(
            [Session(1), Session(2, cancelled: true), Session(3, absent: Ann.Id)],
            Squads(1, [Ann]));

        Assert.Equal(2, attendance.Held);
        Assert.Equal(1, attendance.Cancelled);
        Assert.Equal(2, attendance.Players.Single().Held);
    }

    [Fact]
    public void A_player_is_only_measured_against_the_sessions_of_the_seasons_she_was_in()
    {
        var squads = new SeasonSquads(
        [
            .. TestData.Squad(1, [Ann]).Members,
            .. TestData.Squad(2, [Ann, Bea]).Members
        ]);

        var attendance = TrainingAttendanceReport.Build(
            [Session(1), Session(2), Session(3, seasonId: 2, absent: Bea.Id)],
            squads);

        // Bea joined for the second season: the two evenings before that are not hers to have missed.
        Assert.Equal(1, attendance.Players.Single(p => p.Player.Id == Bea.Id).Held);
        Assert.Equal(3, attendance.Players.Single(p => p.Player.Id == Ann.Id).Held);
    }

    [Fact]
    public void A_guest_was_never_expected_at_a_training()
    {
        // The register offers only full members, so a guest carries no absence and would otherwise read as a perfect attender.
        var attendance = TrainingAttendanceReport.Build(
            [Session(1), Session(2)],
            Squads(1, [Ann, Bea], guestIds: [Bea.Id]));

        Assert.Equal([Ann.Id], attendance.Players.Select(p => p.Player.Id));
    }

    [Fact]
    public void The_squad_figure_weighs_sessions_rather_than_players()
    {
        var squads = new SeasonSquads(
        [
            .. TestData.Squad(1, [Ann]).Members,
            .. TestData.Squad(2, [Ann, Bea]).Members
        ]);

        // Ann: 3 of 3. Bea: 0 of 1. Averaging the two players gives 50%; weighing the four player-sessions gives 75%, and only the
        // second answers "how full were the sessions".
        var attendance = TrainingAttendanceReport.Build(
            [Session(1), Session(2), Session(3, seasonId: 2, absent: Bea.Id)],
            squads);

        Assert.Equal(75, attendance.Percentage);
    }

    [Fact]
    public void The_table_reads_best_attendance_first()
    {
        var cal = TestData.Player(3, "Cal");

        var attendance = TrainingAttendanceReport.Build(
            [Session(1, absent: Bea.Id), Session(2, absent: [Bea.Id, cal.Id])],
            Squads(1, [Ann, Bea, cal]));

        Assert.Equal([Ann.Id, cal.Id, Bea.Id], attendance.Players.Select(p => p.Player.Id));
    }

    [Fact]
    public void A_register_with_nothing_held_in_it_divides_by_nothing()
    {
        var attendance = TrainingAttendanceReport.Build([Session(1, cancelled: true)], Squads(1, [Ann]));

        Assert.Equal(0, attendance.Held);
        Assert.Equal(0, attendance.Percentage);
        Assert.Empty(attendance.Players);
        Assert.Equal(0, TrainingAttendance.Empty.Percentage);
    }

    [Fact]
    public void Someone_in_no_squad_of_the_scope_has_no_sessions_to_have_missed()
    {
        // The player page is reachable for anyone on file, squad member or not — so this is a real answer, not a guard.
        var attendance = TrainingAttendanceReport.BuildFor(Bea, [Session(1), Session(2)], Squads(1, [Ann]));

        Assert.Equal(0, attendance.Held);
        Assert.Equal(0, attendance.Attended);
        Assert.Equal(0, attendance.Percentage);
    }
}
