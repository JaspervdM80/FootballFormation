using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FootballFormation.Core.Tests;

public partial class LocalizationTests
{
    [GeneratedRegex("""L\["((?:[^"\\]|\\.)*)"\s*[,\]]""")]
    private static partial Regex LocalizedLiteral();

    [Fact]
    public void Every_literal_key_in_the_ui_has_a_dutch_entry()
    {
        // A missing entry renders the English key with no warning, so this is the only thing that notices.
        var root = RepositoryRoot();
        var dutch = XDocument.Load(Path.Combine(root, "src", "FootballFormation.UI", "Strings.nl.resx"))
            .Root!.Elements("data")
            .Select(data => (string)data.Attribute("name")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor") || path.EndsWith(".cs"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => LocalizedLiteral().Matches(File.ReadAllText(path))
                .Select(match => (File: Path.GetFileName(path), Key: Regex.Unescape(match.Groups[1].Value))))
            .ToList();

        // Guards the pattern itself: one that matched nothing would pass with every key missing.
        Assert.True(used.Count > 100, $"Only {used.Count} L[\"...\"] keys found — has the pattern stopped matching?");
        Assert.Empty(used.Where(found => !dutch.Contains(found.Key)).Select(found => $"{found.File}: {found.Key}").Distinct());
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FootballFormation.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("FootballFormation.slnx not found above the test output");
    }
}
