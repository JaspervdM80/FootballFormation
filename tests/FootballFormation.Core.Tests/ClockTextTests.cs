namespace FootballFormation.Core.Tests;

/// The dialog's three time fields read and write through this, and it shipped a bug that read "10:4" as 01:04 — a different time, settled
/// silently, where the typist wanted the error. Hence a case for every shape a thumb produces.
public class ClockTextTests
{
    [Theory]
    [InlineData("1045", "10:45")]
    [InlineData("930", "09:30")]
    [InlineData("0000", "00:00")]
    [InlineData("2359", "23:59")]
    [InlineData("9:30", "09:30")]
    [InlineData("10:45", "10:45")]
    [InlineData("09:30", "09:30")]
    [InlineData("  10:45  ", "10:45")]
    public void A_time_a_typist_meant_settles_on_the_24_hour_form(string typed, string expected)
    {
        Assert.Equal(expected, ClockText.Normalize(typed));
    }

    /// Each of these already carries a separator, so the digits alone must not be reshaped: "10:4" would otherwise become 01:04.
    [Theory]
    [InlineData("10:4")]
    [InlineData("12:5")]
    [InlineData("7:30pm")]
    [InlineData("1:2:3")]
    public void A_half_typed_time_comes_back_as_typed_rather_than_as_a_different_one(string typed)
    {
        Assert.Equal(typed, ClockText.Normalize(typed));
        Assert.Null(ClockText.Parse(ClockText.Normalize(typed)));
    }

    /// TimeSpan.TryParse would take the first two as durations, and the kick-off field would roll the match a day forward.
    [Theory]
    [InlineData("2500")]
    [InlineData("24:00")]
    [InlineData("99")]
    [InlineData("abc")]
    [InlineData("25:61")]
    public void Something_that_is_not_a_time_of_day_is_refused(string typed)
    {
        Assert.Null(ClockText.Parse(ClockText.Normalize(typed)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_is_no_time_at_all(string? typed)
    {
        Assert.Null(ClockText.Normalize(typed));
        Assert.Null(ClockText.Parse(typed));
    }

    [Fact]
    public void A_stored_time_is_written_back_in_the_shape_the_field_reads()
    {
        Assert.Equal("09:05", ClockText.Of(new TimeSpan(9, 5, 0)));
        Assert.Equal("23:59", ClockText.Of(new TimeSpan(23, 59, 0)));
        Assert.Null(ClockText.Of(null));
    }

    [Fact]
    public void Normalizing_what_was_already_normalized_changes_nothing()
    {
        var once = ClockText.Normalize("930");
        Assert.Equal(once, ClockText.Normalize(once));
    }

    /// The round trip the dialog actually performs: type, store, reopen.
    [Fact]
    public void A_typed_time_survives_being_stored_and_shown_again()
    {
        var stored = ClockText.Parse(ClockText.Normalize("1045"));

        Assert.Equal(new TimeSpan(10, 45, 0), stored);
        Assert.Equal("10:45", ClockText.Of(stored));
    }

    /// The example is what the placeholder shows, so a reader typing exactly what they are shown has to be right.
    [Fact]
    public void The_placeholder_example_is_itself_a_time_the_field_accepts()
    {
        Assert.Equal(ClockText.Example, ClockText.Normalize(ClockText.Example));
        Assert.NotNull(ClockText.Parse(ClockText.Example));
    }
}
