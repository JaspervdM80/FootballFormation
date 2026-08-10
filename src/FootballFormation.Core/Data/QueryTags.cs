namespace FootballFormation.Core.Data;

/// <summary>
/// Tags attached to a query with <c>TagWith</c>. EF emits one as a comment above the generated
/// SQL, which makes it a marker the query carries with it wherever the SQL is read.
/// </summary>
public static class QueryTags
{
    /// <summary>
    /// Says that a query compares a <see cref="DateTime"/> in SQL <em>on purpose</em>.
    /// <para>
    /// Every date column in this schema is TEXT — SQLite has no date type — so comparing one in
    /// SQL compares the string it was written as, and a row formatted with an ISO <c>T</c>
    /// separator sorts wrongly. The rule is to materialise first and compare the parsed
    /// <see cref="DateTime"/>; <c>DateInSqlInterceptor</c> in the test suite fails any query that
    /// does not, and this tag is the only way past it.
    /// </para>
    /// <para>
    /// Adding it to a new query is a decision to be argued for, not a way to quiet a failing test.
    /// The one holder today is <c>LiveMatchService.GetTodaysMatchAsync</c>'s same-day window, kept
    /// in SQL so the home page does not load the games table whole on every hit.
    /// </para>
    /// </summary>
    public const string ComparesDatesInSql = "compares dates in SQL on purpose (see QueryTags)";
}
