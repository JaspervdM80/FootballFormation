namespace FootballFormation.Core.Data;

public static class QueryTags
{
    /// Every date column in this schema is TEXT — SQLite has no date type — so comparing one in SQL compares the string it was written
    /// as. Materialise first; DateInSqlInterceptor fails any query that does not, and this tag is the only way past it, not a quick fix.
    public const string ComparesDatesInSql = "compares dates in SQL on purpose (see QueryTags)";
}
