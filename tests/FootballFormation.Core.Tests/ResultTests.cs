namespace FootballFormation.Core.Tests;

public class ResultTests
{
    [Fact]
    public void A_message_with_no_placeholders_is_its_own_key()
    {
        var result = Result.Failure("Game not found");

        Assert.True(result.IsFailure);
        Assert.Equal("Game not found", result.Error);
        Assert.Equal("Game not found", result.ErrorKey);
        Assert.Empty(result.ErrorArgs);
    }

    [Fact]
    public void A_templated_message_keeps_the_template_as_the_key_and_formats_the_display_text()
    {
        var result = Result.Failure("Season {0} still has {1} games", "2025/26", 9);

        // The template is what the localizer looks up; Error is the English fallback.
        Assert.Equal("Season {0} still has {1} games", result.ErrorKey);
        Assert.Equal("Season 2025/26 still has 9 games", result.Error);
        Assert.Equal(["2025/26", 9], result.ErrorArgs);
    }

    [Fact]
    public void A_successful_result_carries_no_error()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorKey);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws_rather_than_returning_null()
    {
        var result = Result.Failure<string>("Player with ID {0} not found", 7);

        // Silently handing back default is how a skipped success check becomes a null three
        // frames away. This is the guard that stops it.
        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Player with ID 7 not found", ex.Message);
    }

    [Fact]
    public void Carrying_a_failure_to_another_type_keeps_the_key_and_arguments()
    {
        var original = Result.Failure<int>("Season {0} has no squad to copy", "2024/25");

        var carried = original.To<string>();

        Assert.True(carried.IsFailure);
        Assert.Equal(original.ErrorKey, carried.ErrorKey);
        Assert.Equal(original.ErrorArgs, carried.ErrorArgs);
        Assert.Equal(original.Error, carried.Error);
    }

    [Fact]
    public void A_cancelled_result_is_a_failure_with_nothing_to_say()
    {
        var result = Result.Cancelled();

        // A failure, so every "did that work?" check reads it as no — but with nothing to show.
        Assert.True(result.IsCancelled);
        Assert.True(result.IsFailure);
        Assert.Null(result.ErrorKey);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Carrying_a_cancellation_to_another_type_keeps_it_a_cancellation()
    {
        // Drop the flag here and an abandoned call arrives as a messageless failure — an empty snackbar.
        var carried = Result.Cancelled<int>().To<string>();

        Assert.True(carried.IsCancelled);
        Assert.True(carried.IsFailure);
        Assert.Null(carried.ErrorKey);
    }

    [Fact]
    public void Reading_the_value_of_a_cancelled_result_says_the_caller_went_away()
    {
        var result = Result.Cancelled<string>();

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatting_is_culture_invariant_so_the_key_and_the_arguments_stay_separable()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Dutch writes decimals with a comma; the English fallback must not shift with it.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");

            Assert.Equal("Season 1.5 still has 9 games",
                Result.Failure("Season {0} still has {1} games", 1.5, 9).Error);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
