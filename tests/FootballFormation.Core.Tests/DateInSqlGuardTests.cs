using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// The guard itself. Every other suite exercises it by accident — these say what it catches, so a
/// change that quietly stops it catching anything fails here rather than passing everywhere.
/// </summary>
public class DateInSqlGuardTests : ServiceTestBase
{
    [Fact]
    public void Every_date_column_in_the_schema_is_watched()
    {
        Assert.Equal(
            ["ClockRunningSince", "CreatedAt", "Date", "EditedAt", "EndDate", "RecordedAt", "StartDate"],
            DateInSqlInterceptor.DateColumns.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ordering_by_a_date_in_SQL_is_refused()
    {
        await SeedSeasonAsync();

        var refused = await Assert.ThrowsAsync<DateComparedInSqlException>(
            () => Read().Seasons.OrderBy(s => s.StartDate).ToListAsync());

        Assert.Contains("ORDER BY \"StartDate\"", refused.Message);
    }

    [Fact]
    public async Task Comparing_a_date_in_SQL_is_refused()
    {
        await SeedSeasonAsync();

        var refused = await Assert.ThrowsAsync<DateComparedInSqlException>(
            () => Read().Seasons.Where(s => s.StartDate < Now).ToListAsync());

        Assert.Contains("comparison on \"StartDate\"", refused.Message);
    }

    [Fact]
    public async Task Ordering_the_rows_after_materialising_them_is_the_way_through()
    {
        var season = await SeedSeasonAsync();

        var seasons = (await Read().Seasons.ToListAsync()).NewestFirst();

        Assert.Equal(season.Id, seasons[0].Id);
    }

    [Fact]
    public async Task The_home_pages_same_day_window_is_allowed_because_it_says_so()
    {
        // LiveMatchService keeps this one comparison in SQL on purpose and tags it. That the call
        // returns at all is the assertion: without the tag the interceptor would refuse it.
        var season = await SeedSeasonAsync();
        Db.Games.Add(new Game { SeasonId = season.Id, Opponent = "Today", Date = Now });
        await Db.SaveChangesAsync();

        var game = await Live.GetTodaysMatchAsync();

        Assert.True(game.IsSuccess);
        Assert.Equal("Today", game.Value!.Opponent);
    }

    [Fact]
    public void A_tagged_query_is_the_only_way_past_the_rule()
    {
        const string offending =
            """"
            SELECT "s"."StartDate" FROM "Seasons" AS "s" ORDER BY "s"."StartDate"
            """";

        Assert.NotEmpty(DateInSqlInterceptor.Violations(offending));
        Assert.Empty(DateInSqlInterceptor.Violations(
            $"-- {QueryTags.ComparesDatesInSql}{Environment.NewLine}{offending}"));
    }

    [Fact]
    public void A_query_that_touches_no_date_is_left_alone()
    {
        Assert.Empty(DateInSqlInterceptor.Violations(
            """"
            SELECT "p"."Id" FROM "Players" AS "p" WHERE "p"."ShirtNumber" > @__n_0 ORDER BY "p"."Surname"
            """"));
    }

    [Fact]
    public void Picking_MAX_of_a_date_column_in_SQL_is_refused()
    {
        var violations = DateInSqlInterceptor.Violations(
            """"
            SELECT MAX("g"."Date") FROM "Games" AS "g"
            """");

        Assert.Contains("MIN/MAX of \"Date\"", violations);
    }

    [Fact]
    public void Picking_MIN_of_a_date_column_in_SQL_is_refused()
    {
        var violations = DateInSqlInterceptor.Violations(
            """"
            SELECT MIN("Date") FROM "Games" AS "g"
            """");

        Assert.Contains("MIN/MAX of \"Date\"", violations);
    }

    [Fact]
    public void A_BETWEEN_window_on_a_date_column_in_SQL_is_refused()
    {
        var violations = DateInSqlInterceptor.Violations(
            """"
            SELECT "g"."Id" FROM "Games" AS "g" WHERE "g"."Date" BETWEEN @__today_0 AND @__tomorrow_1
            """");

        Assert.Contains("BETWEEN on \"Date\"", violations);
    }

    [Fact]
    public void A_date_column_as_either_operand_of_BETWEEN_is_refused()
    {
        var firstOperand = DateInSqlInterceptor.Violations(
            """"
            SELECT "s"."Id" FROM "Seasons" AS "s" WHERE @__day_0 BETWEEN "s"."StartDate" AND @__x_1
            """");
        Assert.Contains("BETWEEN on \"StartDate\"", firstOperand);

        var secondOperand = DateInSqlInterceptor.Violations(
            """"
            SELECT "s"."Id" FROM "Seasons" AS "s" WHERE @__day_0 BETWEEN "s"."StartDate" AND "s"."EndDate"
            """");
        Assert.Contains("BETWEEN on \"StartDate\"", secondOperand);
        Assert.Contains("BETWEEN on \"EndDate\"", secondOperand);
    }

    [Fact]
    public void A_query_with_no_date_column_survives_MIN_MAX_and_BETWEEN_too()
    {
        Assert.Empty(DateInSqlInterceptor.Violations(
            """"
            SELECT MAX("p"."ShirtNumber") FROM "Players" AS "p"
            WHERE "p"."Id" BETWEEN @__a_0 AND @__b_1
            """"));
    }
}
