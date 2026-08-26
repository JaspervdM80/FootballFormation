namespace FootballFormation.Core.Tests;

public class SeasonTests
{
    [Theory]
    // The July boundary is the whole point of the type: 30 June still belongs to the season that
    // opened the previous July, and 1 July opens the next one.
    [InlineData("2025-06-30", 2024)]
    [InlineData("2025-07-01", 2025)]
    [InlineData("2025-08-15", 2025)]
    [InlineData("2025-12-31", 2025)]
    [InlineData("2026-01-01", 2025)]
    [InlineData("2026-06-30", 2025)]
    [InlineData("2026-07-01", 2026)]
    public void StartYearFor_puts_the_date_in_the_season_that_opened_in_July(string date, int expected) =>
        Assert.Equal(expected, Season.StartYearFor(DateTime.Parse(date)));

    [Theory]
    [InlineData(2025, "2025/26")]
    [InlineData(2029, "2029/30")]
    // The two-digit wrap: 2099/00, not 2099/100.
    [InlineData(2099, "2099/00")]
    public void NameForStartYear_writes_a_two_digit_closing_year(int startYear, string expected) =>
        Assert.Equal(expected, Season.NameForStartYear(startYear));

    [Fact]
    public void CreateFor_covers_exactly_one_July_to_June_window()
    {
        var season = Season.CreateFor(new DateTime(2026, 3, 14));

        Assert.Equal("2025/26", season.Name);
        Assert.Equal(new DateTime(2025, 7, 1), season.StartDate);
        Assert.Equal(new DateTime(2026, 6, 30), season.EndDate);
    }

    [Fact]
    public void Consecutive_seasons_leave_no_gap()
    {
        var first = Season.CreateFor(new DateTime(2025, 9, 1));
        var second = Season.CreateFor(new DateTime(2026, 9, 1));

        // Gaplessness is what lets Game.SeasonId be required — a date between two seasons would
        // orphan a game. This is the invariant CloseSeasonGapsAsync exists to repair.
        Assert.Equal(first.EndDate.AddDays(1), second.StartDate);
    }

    [Fact]
    public void Contains_ignores_the_time_component()
    {
        var season = Season.CreateFor(new DateTime(2025, 9, 1));

        // Game dates come from a picker and carry midnight; the last day must still be inside.
        Assert.True(season.Contains(new DateTime(2026, 6, 30, 23, 59, 0)));
        Assert.True(season.Contains(new DateTime(2025, 7, 1, 0, 0, 0)));
        Assert.False(season.Contains(new DateTime(2026, 7, 1)));
        Assert.False(season.Contains(new DateTime(2025, 6, 30)));
    }

    [Fact]
    public void ShortName_trims_the_century_only_when_there_is_one_to_trim()
    {
        Assert.Equal("25/26", new Season { Name = "2025/26" }.ShortName);
        // A hand-edited name is left alone rather than mangled.
        Assert.Equal("Najaar", new Season { Name = "Najaar" }.ShortName);
    }
}
