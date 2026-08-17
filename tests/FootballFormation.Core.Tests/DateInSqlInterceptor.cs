using System.Data.Common;
using System.Text.RegularExpressions;
using FootballFormation.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FootballFormation.Core.Tests;

/// <summary>
/// Fails any query that orders or compares a <see cref="DateTime"/> in SQL — the rule, and why it
/// matters, are on <see cref="QueryTags.ComparesDatesInSql"/>.
/// <para>
/// Registered on the context factory in <see cref="ServiceTestBase"/>, so it watches every query
/// the whole suite makes rather than the handful a dedicated test would remember to call. The
/// failure it catches is silent and produces plausible output — a slightly wrong order nobody
/// notices until a backup is restored — which is why prose alone was not enough.
/// </para>
/// </summary>
public sealed class DateInSqlInterceptor : DbCommandInterceptor
{
    /// <summary>
    /// Every date column in the schema, read from the EF model rather than listed by hand — a new
    /// one is covered the moment it is mapped, which a hand-written list would not manage.
    /// </summary>
    public static readonly IReadOnlySet<string> DateColumns = DiscoverDateColumns();

    // EF puts ORDER BY at the end of a SELECT, ahead of LIMIT/OFFSET. Matching to one of those
    // keywords rather than to end-of-string keeps a subquery's clause from swallowing the rest.
    private static readonly Regex OrderByClause = new(
        @"ORDER\s+BY(?<clause>.*?)(?=\bLIMIT\b|\bOFFSET\b|\bUNION\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // "g"."Date" >= @__today_0 — the quoted identifier immediately left of the operator.
    private static readonly Regex ComparedOnTheLeft = new(
        """
        "(?<col>[^"]+)"\s*(?:<=|>=|<|>)
        """,
        RegexOptions.Compiled);

    // @__today_0 < "g"."Date" — and the one immediately right of it.
    private static readonly Regex ComparedOnTheRight = new(
        """
        (?:<=|>=|<|>)\s*(?:"[^"]+"\.)?"(?<col>[^"]+)"
        """,
        RegexOptions.Compiled);

    // MAX("g"."Date") / MIN("Date") — picks by string order and emits no operator at all.
    private static readonly Regex MinMaxOfColumn = new(
        """
        (?:MAX|MIN)\s*\(\s*(?:"[^"]+"\.)?"(?<col>[^"]+)"\s*\)
        """,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "g"."Date" BETWEEN @__today_0 AND @__tomorrow_1 — compares two dates with no operator either.
    private static readonly Regex BetweenColumnBefore = new(
        """
        "(?<col>[^"]+)"\s+BETWEEN\s
        """,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // @__today_0 BETWEEN "s"."StartDate" AND @__x — the column immediately after BETWEEN.
    private static readonly Regex BetweenColumnFirstOperand = new(
        """
        \bBETWEEN\b\s*(?:"[^"]+"\.)?"(?<col>[^"]+)"
        """,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // @__today_0 BETWEEN "s"."StartDate" AND "s"."EndDate" — the column after the AND.
    private static readonly Regex BetweenColumnSecondOperand = new(
        """
        \bBETWEEN\b.{0,80}?\bAND\b\s*(?:"[^"]+"\.)?"(?<col>[^"]+)"
        """,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Guard(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Guard(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Guard(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Guard(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Guard(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(command.CommandText);
        return ValueTask.FromResult(result);
    }

    /// <summary>The offending columns, or empty when the SQL is clean. Public so a test can state
    /// what the rule catches without having to provoke a real query.</summary>
    public static IReadOnlyList<string> Violations(string sql)
    {
        if (sql.Contains(QueryTags.ComparesDatesInSql, StringComparison.Ordinal)) return [];

        var found = new List<string>();

        foreach (Match clause in OrderByClause.Matches(sql))
        {
            foreach (var column in DateColumns)
            {
                if (clause.Groups["clause"].Value.Contains($"\"{column}\"", StringComparison.Ordinal))
                    found.Add($"ORDER BY \"{column}\"");
            }
        }

        foreach (var regex in new[] { ComparedOnTheLeft, ComparedOnTheRight })
        {
            foreach (Match match in regex.Matches(sql))
            {
                var column = match.Groups["col"].Value;
                if (DateColumns.Contains(column)) found.Add($"comparison on \"{column}\"");
            }
        }

        foreach (Match match in MinMaxOfColumn.Matches(sql))
        {
            var column = match.Groups["col"].Value;
            if (DateColumns.Contains(column)) found.Add($"MIN/MAX of \"{column}\"");
        }

        foreach (var regex in new[] { BetweenColumnBefore, BetweenColumnFirstOperand, BetweenColumnSecondOperand })
        {
            foreach (Match match in regex.Matches(sql))
            {
                var column = match.Groups["col"].Value;
                if (DateColumns.Contains(column)) found.Add($"BETWEEN on \"{column}\"");
            }
        }

        return [.. found.Distinct()];
    }

    private static void Guard(string sql)
    {
        var violations = Violations(sql);
        if (violations.Count == 0) return;

        throw new DateComparedInSqlException(
            $"""
             This query sorts or compares a date in SQL: {string.Join(", ", violations)}.

             SQLite stores dates as TEXT, so SQL compares the string the value happened to be
             written as — correct only while every row carries identical formatting, and wrong the
             moment a restored backup uses an ISO 'T' separator.

             Materialise first and order the objects: GameOrdering.NewestFirst / OldestFirst
             (Models/Game.cs) and SeasonOrdering (Models/Season.cs) do it with the tie-break spelled
             out. If the comparison genuinely has to stay in SQL, say so with
             .TagWith(QueryTags.ComparesDatesInSql) and record why.

             {sql}
             """);
    }

    private static HashSet<string> DiscoverDateColumns()
    {
        // The model is built from the mappings alone, so this needs no database behind it.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Filename=:memory:").Options);

        return
        [
            .. db.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties())
                .Where(property =>
                    (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime))
                .Select(property => property.GetColumnName())
                .Where(column => !string.IsNullOrEmpty(column))
        ];
    }
}

/// <summary>Its own type so a test can assert on the rule rather than on a message.</summary>
public sealed class DateComparedInSqlException(string message) : Exception(message);
