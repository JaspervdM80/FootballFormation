using System.Text.RegularExpressions;

namespace FootballFormation.Core.Tests;

/// CLAUDE.md, the skills and the reviewer agent are read by an agent that believes them, and they drift as the code moves — a renamed
/// member, a moved file, a route nobody added to the table. This is selectors.spec.js for the instructions: every file and code name
/// they quote in backticks has to still exist.
public partial class InstructionReferenceTests
{
    /// Named on purpose although absent: history the instructions tell the reader not to look for, or not to bring back.
    private static readonly HashSet<string> AbsentByDesign =
    [
        "AddSeasons", "AddSeasonSquads", "StoreGoalPeriodAndClock", "ConsolidatePlayerPositions",
        "PauseClockAsync", "ResumeClockAsync", "gotoRendered", "waitForHandlers", "OnChanged", "IPlayerService",
        "ShowMessageBox",
    ];

    /// Named from outside this code — MSBuild, HTTP, MudBlazor, xUnit, a placeholder — so there is nothing here to find them in.
    private static readonly HashSet<string> FromElsewhere =
    [
        "GenerateDocumentationFile", "GenerateResource", "Referer", "MudPopover", "Validate", "TestContext", "IsAuthenticated",
        "Arrange", "Foo", "dotnet-install.sh", "admin/admin",
    ];

    private static readonly string[] BranchPrefixes = ["origin/", "feature/", "bug/", "ci/"];

    private static readonly (string Short, string Full)[] ProjectShorthands =
        [("Core/", "FootballFormation.Core/"), ("UI/", "FootballFormation.UI/"), ("Web/", "FootballFormation.Web/")];

    [GeneratedRegex("```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeFence();

    [GeneratedRegex("`([^`\n]+)`")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^(?:[A-Z][A-Za-z0-9]*|[a-z]+[A-Z][A-Za-z0-9]*)(?:\.[A-Za-z][A-Za-z0-9]*)*(?:\(\))?$")]
    private static partial Regex CodeName();

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex GenericArguments();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"@page\s+""([^""]+)""")]
    private static partial Regex PageDirective();

    [GeneratedRegex(@"\{[A-Za-z]+Id(?::int)?\}")]
    private static partial Regex RouteId();

    private static readonly string[] FileExtensions =
        [".cs", ".razor", ".css", ".js", ".mjs", ".sh", ".json", ".md", ".resx", ".props", ".toml", ".yml", ".slnx", ".txt"];

    [Fact]
    public void Every_file_the_instructions_quote_still_exists()
    {
        var paths = RepositoryPaths();

        var missing = Quotes()
            .Where(quote => IsPath(quote.Text) && !FromElsewhere.Contains(quote.Text))
            .Where(quote => !paths.Any(path => path == Normalise(quote.Text) || path.EndsWith("/" + Normalise(quote.Text))))
            .Select(quote => $"{quote.Source}: {quote.Text}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, $"Quoted paths that no longer exist: {string.Join("; ", missing)}");
    }

    [Fact]
    public void Every_code_name_the_instructions_quote_still_exists()
    {
        var words = CodeWords();

        var missing = Quotes()
            .Select(quote => quote with { Text = GenericArguments().Replace(quote.Text, "") })
            .Where(quote => CodeName().IsMatch(quote.Text))
            .SelectMany(quote => quote.Text.TrimEnd('(', ')').Split('.')
                .Where(segment => !words.Contains(segment) && !AbsentByDesign.Contains(segment) && !FromElsewhere.Contains(segment))
                .Select(segment => $"{quote.Source}: {segment} (in `{quote.Text}`)"))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, $"Quoted names found nowhere in the code: {string.Join("; ", missing)}");
    }

    [Fact]
    public void Every_name_kept_absent_on_purpose_is_still_quoted_and_still_absent()
    {
        var quoted = Quotes().SelectMany(quote => quote.Text.Split('.', '(', ')', ' ')).ToHashSet();
        var words = CodeWords();

        var stale = AbsentByDesign.Where(name => !quoted.Contains(name) || words.Contains(name)).ToList();

        Assert.True(stale.Count == 0, $"Drop these from AbsentByDesign: {string.Join(", ", stale)}");
    }

    [Fact]
    public void Every_page_route_has_a_row_in_the_verify_ui_table()
    {
        // The first cell, not anywhere in the file: the prose names routes too, and a route it mentions is not one anybody checks.
        var firstCells = File.ReadLines(Repository.PathTo(".claude", "skills", "verify-ui", "SKILL.md"))
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Split('|')[1])
            .ToList();

        var missing = PageRoutes().Where(route => !firstCells.Any(cell => cell.Contains($"`{route}`"))).ToList();

        Assert.True(missing.Count == 0, $"verify-ui's route table has no row for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_page_route_is_in_the_render_mode_split()
    {
        var text = File.ReadAllText(Repository.PathTo(".claude", "skills", "razor-pages-and-circuit", "SKILL.md"));

        var missing = PageRoutes().Where(route => !text.Contains($"`{route}`")).ToList();

        Assert.True(missing.Count == 0, $"razor-pages-and-circuit's render-mode split leaves out: {string.Join(", ", missing)}");
    }

    private static List<string> PageRoutes()
    {
        var routes = Repository.FilesUnder("src", ".razor")
            .SelectMany(path => PageDirective().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Select(route => RouteId().Replace(route, "{id}"))
            .ToList();

        Assert.True(routes.Count > 15, $"Only {routes.Count} @page routes found — has the pattern stopped matching?");
        return routes;
    }

    [Fact]
    public void Every_skill_is_named_after_its_folder()
    {
        var misnamed = Repository.FilesUnder(Path.Combine(".claude", "skills"), "SKILL.md")
            .Where(path => !File.ReadLines(path).Contains($"name: {Path.GetFileName(Path.GetDirectoryName(path))}"))
            .ToList();

        Assert.Empty(misnamed);
    }

    [Fact]
    public void The_scan_reads_the_instructions_it_is_meant_to_judge()
    {
        // Guards the scan itself: one that read no quotes would pass with every reference broken.
        var quotes = Quotes().ToList();

        Assert.True(quotes.Count > 500, $"Only {quotes.Count} quoted spans found — have the instruction files moved?");
    }

    private sealed record Quote(string Source, string Text);

    private static IEnumerable<Quote> Quotes() =>
        Repository.FilesUnder(Path.Combine(".claude", "skills"), ".md")
            .Concat(Repository.FilesUnder(Path.Combine(".claude", "agents"), ".md"))
            .Append(Repository.PathTo("CLAUDE.md"))
            .SelectMany(path => Quoted().Matches(CodeFence().Replace(File.ReadAllText(path), ""))
                .Select(match => new Quote(Path.GetRelativePath(Repository.Root, path), match.Groups[1].Value.Trim())));

    private static bool IsPath(string text) =>
        !text.Contains(' ') && !text.StartsWith('/') && !text.StartsWith('~') && !text.Contains('%') && !text.Contains('*')
        && !text.Contains('…') && !text.Contains('<') && !text.Contains("://") && !text.StartsWith("_content/") && !text.StartsWith("artifacts/")
        && !BranchPrefixes.Any(text.StartsWith)
        && (text.Contains('/') || FileExtensions.Any(extension => text.EndsWith(extension) && text.Length > extension.Length && !text.StartsWith('.')))
        && !CodeName().IsMatch(text);

    private static string Normalise(string path)
    {
        var normalised = path.Replace('\\', '/').TrimEnd('/');
        var shorthand = ProjectShorthands.FirstOrDefault(pair => normalised.StartsWith(pair.Short));
        return shorthand.Short is null ? normalised : shorthand.Full + normalised[shorthand.Short.Length..];
    }

    private static HashSet<string> RepositoryPaths()
    {
        var files = Repository.FilesUnder("")
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .ToList();

        var folders = files.SelectMany(file => Enumerable.Range(1, file.Count(c => c == '/'))
            .Select(depth => string.Join('/', file.Split('/').Take(depth))));

        return [.. files, .. folders];
    }

    /// Leaves out the files that name the absent on purpose, or AbsentByDesign would find them there — and puts their class names back.
    private static readonly string[] NamesTheAbsent = [nameof(InstructionReferenceTests), nameof(UiHarnessRulesTests)];

    private static HashSet<string> CodeWords() =>
        [.. NamesTheAbsent, .. Repository.FilesUnder("")
            .Where(path => !NamesTheAbsent.Any(name => path.EndsWith(name + ".cs")))
            .Where(path => new[] { ".cs", ".razor", ".css", ".js", ".mjs", ".json", ".yml", ".sh", ".resx", ".props", ".csproj", ".txt" }.Any(path.EndsWith))
            .SelectMany(path => Word().Matches(File.ReadAllText(path)).Select(match => match.Value))];
}
