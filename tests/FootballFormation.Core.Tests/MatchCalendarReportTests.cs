using System.Text;

namespace FootballFormation.Core.Tests;

public class MatchCalendarReportTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_fully_arranged_home_game_starts_at_the_meet_time_and_ends_after_the_match_and_half_time()
    {
        var game = TestData.Game(id: 42, date: new DateTime(2026, 3, 14, 10, 0, 0));
        game.MeetTime = new TimeSpan(9, 15, 0);
        game.WarmUpTime = new TimeSpan(9, 30, 0);
        game.SportsPark = "Sportpark De Wetering";
        game.FieldName = "3";
        game.City = "Gouda";

        var lines = Unfold(Build(game));

        Assert.Contains("UID:game-42@footballformation", lines);
        Assert.Contains("DTSTAMP:20260301T093000Z", lines);
        Assert.Contains("DTSTART;TZID=Europe/Amsterdam:20260314T091500", lines);
        Assert.Contains("DTEND;TZID=Europe/Amsterdam:20260314T111500", lines);
        Assert.Contains("SUMMARY:GJS MO15-2 – Opponent", lines);
        Assert.Contains("LOCATION:Sportpark De Wetering\\, Gouda", lines);
        Assert.Contains("DESCRIPTION:info for 42", lines);
        Assert.Contains("URL:https://example.test/games/42/overview", lines);
    }

    [Fact]
    public void A_game_with_only_a_kick_off_and_a_field_starts_at_the_kick_off_and_carries_no_location()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 10, 0, 0), durationMinutes: 50);
        game.FieldName = "5";

        var lines = Unfold(Build(game));

        Assert.Contains("DTSTART;TZID=Europe/Amsterdam:20260314T100000", lines);
        Assert.Contains("DTEND;TZID=Europe/Amsterdam:20260314T110500", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("LOCATION"));
    }

    [Fact]
    public void A_game_with_only_a_meet_time_runs_from_the_meet_time()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14));
        game.MeetTime = new TimeSpan(8, 30, 0);

        var lines = Unfold(Build(game));

        Assert.Contains("DTSTART;TZID=Europe/Amsterdam:20260314T083000", lines);
        Assert.Contains("DTEND;TZID=Europe/Amsterdam:20260314T094500", lines);
    }

    [Fact]
    public void A_game_with_no_times_at_all_is_an_all_day_event()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14));

        var lines = Unfold(Build(game));

        Assert.Contains("DTSTART;VALUE=DATE:20260314", lines);
        Assert.Contains("DTEND;VALUE=DATE:20260315", lines);
    }

    // A meet time after kick-off is a typo, and an event that starts after its own match would hide the kick-off.
    [Fact]
    public void A_meet_time_after_kick_off_never_moves_the_start_past_the_kick_off()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 10, 0, 0));
        game.MeetTime = new TimeSpan(11, 0, 0);

        Assert.Equal(new DateTime(2026, 3, 14, 10, 0, 0), MatchCalendarReport.TimesOf(game)!.Value.Start);
    }

    [Fact]
    public void An_away_game_names_the_home_side_first_and_a_played_one_carries_the_scoreline_in_venue_order()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 10, 0, 0));
        game.IsHomeGame = false;
        game.ScoreHome = 3;
        game.ScoreAway = 1;

        Assert.Equal("Opponent 1-3 GJS MO15-2", MatchCalendarReport.SummaryOf(game, "GJS MO15-2"));

        game.ScoreHome = null;
        game.ScoreAway = null;
        Assert.Equal("Opponent – GJS MO15-2", MatchCalendarReport.SummaryOf(game, "GJS MO15-2"));
    }

    [Fact]
    public void Commas_semicolons_backslashes_and_newlines_are_escaped()
    {
        Assert.Equal("a\\, b\\; c\\\\d\\ne\\nf", MatchCalendarReport.Escape("a, b; c\\d\r\ne\nf"));
    }

    [Fact]
    public void Every_line_ends_in_crlf_and_no_line_is_longer_than_75_octets_even_through_emoji()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 10, 0, 0));
        var description = string.Concat(Enumerable.Repeat("⚽ Kleedkamer: ouders van Anna, 🚩 vlaggen ", 6));

        var ics = MatchCalendarReport.Build("Us", [game], _ => description, _ => "https://example.test", Now);

        Assert.EndsWith("\r\n", ics);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));

        var physical = ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(physical, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, l));

        // Unfolding must give back exactly what went in: a fold that split a UTF-8 sequence would not.
        Assert.Contains($"DESCRIPTION:{MatchCalendarReport.Escape(description)}", Unfold(ics));
    }

    [Fact]
    public void The_calendar_declares_the_time_zone_its_events_name_and_lists_games_oldest_first()
    {
        var later = TestData.Game(id: 2, date: new DateTime(2026, 3, 21, 10, 0, 0));
        var sooner = TestData.Game(id: 1, date: new DateTime(2026, 3, 7, 10, 0, 0));

        var ics = MatchCalendarReport.Build("GJS MO15-2", [later, sooner], _ => "", _ => "https://example.test", Now);
        var lines = Unfold(ics);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Contains("TZID:Europe/Amsterdam", lines);
        Assert.Contains("X-WR-CALNAME:GJS MO15-2", lines);
        Assert.True(ics.IndexOf("UID:game-1@", StringComparison.Ordinal) < ics.IndexOf("UID:game-2@", StringComparison.Ordinal));
    }

    private static string Build(Game game) => MatchCalendarReport.Build(
        "GJS MO15-2",
        [game],
        g => $"info for {g.Id}",
        g => $"https://example.test/games/{g.Id}/overview",
        Now);

    private static List<string> Unfold(string ics) =>
        [.. ics.Replace("\r\n ", "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];
}
