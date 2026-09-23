namespace FootballFormation.Core.Tests;

public class DutyRosterReportTests
{
    private static readonly DateTime Today = new(2026, 9, 23);

    private static Game GameOn(int id, DateTime date, string? dressingRoom = null, string? flags = null, string? wash = null)
    {
        var game = TestData.Game(id: id, date: date);
        game.Opponent = $"Opponent {id}";
        game.DressingRoomDuty = dressingRoom;
        game.FlagDuty = flags;
        game.WashDuty = wash;
        return game;
    }

    [Fact]
    public void Games_are_listed_oldest_first()
    {
        var roster = DutyRosterReport.Build(
            [GameOn(1, Today.AddDays(14)), GameOn(2, Today.AddDays(-7)), GameOn(3, Today.AddDays(7))],
            Today);

        Assert.Equal([2, 3, 1], roster.Rows.Select(r => r.Game.Id));
    }

    [Fact]
    public void The_first_game_from_today_on_is_next_and_the_ones_before_it_are_past()
    {
        var roster = DutyRosterReport.Build(
            [GameOn(1, Today.AddDays(-7)), GameOn(2, Today.AddDays(7)), GameOn(3, Today.AddDays(14))],
            Today);

        Assert.Equal(
            [DutyTiming.Past, DutyTiming.Next, DutyTiming.Upcoming],
            roster.Rows.Select(r => r.Timing));
    }

    /// The duties run until the day is over, so a match that has already kicked off this morning is still the one to show.
    [Fact]
    public void A_game_earlier_today_is_still_next()
    {
        var roster = DutyRosterReport.Build(
            [GameOn(1, Today.AddHours(9)), GameOn(2, Today.AddDays(7))],
            Today.AddHours(15));

        Assert.Equal(DutyTiming.Next, roster.Rows[0].Timing);
        Assert.Equal(DutyTiming.Upcoming, roster.Rows[1].Timing);
    }

    [Fact]
    public void Two_games_on_the_same_day_make_only_the_one_entered_first_next()
    {
        var roster = DutyRosterReport.Build([GameOn(2, Today), GameOn(1, Today)], Today);

        Assert.Equal(1, roster.Rows.Single(r => r.Timing == DutyTiming.Next).Game.Id);
    }

    [Fact]
    public void A_season_already_played_has_no_next_game()
    {
        var roster = DutyRosterReport.Build([GameOn(1, Today.AddDays(-14)), GameOn(2, Today.AddDays(-7))], Today);

        Assert.All(roster.Rows, r => Assert.Equal(DutyTiming.Past, r.Timing));
    }

    [Fact]
    public void A_duty_nobody_filled_in_all_season_is_not_shown()
    {
        var roster = DutyRosterReport.Build(
            [GameOn(1, Today, flags: "Jansen"), GameOn(2, Today.AddDays(7), wash: "  ")],
            Today);

        Assert.False(roster.ShowsDressingRoom);
        Assert.True(roster.ShowsFlags);
        Assert.False(roster.ShowsWash);
        Assert.True(roster.HasAnyDuty);
    }

    [Fact]
    public void A_season_with_no_duties_entered_has_none_to_show()
    {
        var roster = DutyRosterReport.Build([GameOn(1, Today)], Today);

        Assert.False(roster.HasAnyDuty);
        Assert.Single(roster.Rows);
    }
}
