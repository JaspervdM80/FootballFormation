using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

/// <summary>
/// Stores a small list of ids or enum values as comma-separated text in one column.
/// <para>
/// These are short, always read whole, and never queried by element — a join table would cost a
/// second query for no benefit. The <see cref="ValueComparer{T}"/> is not optional: without it EF
/// compares the list by reference, so mutating one in place is silently never persisted (see
/// docs/known_issues.md).
/// </para>
/// </summary>
internal static class CsvListConverters
{
    public static PropertyBuilder<List<int>> HasCsvListConversion(this PropertyBuilder<List<int>> property) =>
        property.HasConversion(
            list => Join(list),
            text => ParseInts(text),
            Comparer<int>());

    public static PropertyBuilder<List<TEnum>> HasCsvListConversion<TEnum>(
        this PropertyBuilder<List<TEnum>> property)
        where TEnum : struct, Enum =>
        property.HasConversion(
            list => Join(list.Select(value => Convert.ToInt32(value))),
            text => ParseEnums<TEnum>(text),
            Comparer<TEnum>());

    /// <summary>
    /// Structural equality plus a deep copy of the snapshot. EF takes the snapshot when the entity
    /// is loaded and compares it on save; both halves have to see the list's contents, not its
    /// identity, or in-place edits go undetected.
    /// </summary>
    private static ValueComparer<List<T>> Comparer<T>() => new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (hash, value) => HashCode.Combine(hash, value!.GetHashCode())),
        c => c.ToList());

    private static string Join(IEnumerable<int> values) => string.Join(',', values);

    private static List<int> ParseInts(string text) => [.. Parts(text).Select(int.Parse)];

    private static List<TEnum> ParseEnums<TEnum>(string text) where TEnum : struct, Enum =>
        [.. Parts(text).Select(part => (TEnum)Enum.ToObject(typeof(TEnum), int.Parse(part)))];

    private static IEnumerable<string> Parts(string text) =>
        text.Length == 0 ? [] : text.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
