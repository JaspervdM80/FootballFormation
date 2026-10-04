using System.Text.RegularExpressions;

namespace FootballFormation.Core.Tests;

/// The browser suites' rules that a reading of the source can hold, so they fail here in seconds rather than in a review: each one
/// was broken once and cost minutes a run or a test that could not fail. The ui-testing skill says why each exists.
public partial class UiHarnessRulesTests
{
    /// Where `networkidle` is the assertion itself — "no WebSocket was ever opened" — rather than a readiness wait.
    private static readonly HashSet<string> NetworkIdleAsAssertion = ["rendermode.spec.js", "duties.spec.js"];

    /// The fixtures own every browser context in the suite: they carry the console-error check and close it on failure.
    private static readonly HashSet<string> ContextOwners = ["fixtures.js", "global-setup.js"];

    private static readonly string[] RemovedReadinessWaits = ["gotoRendered", "waitForHandlers", "_bl_"];

    [GeneratedRegex(@"waitForTimeout\(|setTimeout\(")]
    private static partial Regex Sleep();

    [GeneratedRegex(@"while \(Date\.now\(\) < deadline\)")]
    private static partial Regex DeadlinePoll();

    [GeneratedRegex(@"const READY = (.+);")]
    private static partial Regex Ready();

    [GeneratedRegex(@"const IGNORED = \[(.*?)\];", RegexOptions.Singleline)]
    private static partial Regex IgnoredList();

    [GeneratedRegex(@"/[^/\n]+/i")]
    private static partial Regex RegexLiteral();

    [Fact]
    public void No_spec_or_helper_sleeps()
    {
        var sleeps = Lines(UiSuite()).Where(line => Sleep().IsMatch(line.Text)).Select(line => line.Site).ToList();

        Assert.True(sleeps.Count == 0, $"A fixed sleep in tests/ui — wait on the outcome instead: {string.Join("; ", sleeps)}");
    }

    [Fact]
    public void A_harness_waits_only_as_the_tick_of_a_deadline_bounded_poll()
    {
        var sleeps = Harness()
            .SelectMany(path =>
            {
                var lines = File.ReadAllLines(path);
                return lines.Select((text, index) => (text, index))
                    .Where(line => Sleep().IsMatch(line.text))
                    .Where(line => !lines[Math.Max(0, line.index - 8)..line.index].Any(DeadlinePoll().IsMatch))
                    .Select(line => $"{Path.GetFileName(path)}:{line.index + 1}");
            })
            .ToList();

        Assert.True(sleeps.Count == 0, $"A wait outside a `while (Date.now() < deadline)` poll is a fixed sleep: {string.Join("; ", sleeps)}");
    }

    [Fact]
    public void Network_idle_is_never_a_readiness_wait()
    {
        var waits = Lines(UiSuite())
            .Where(line => line.Text.Contains("networkidle") && !NetworkIdleAsAssertion.Contains(line.File))
            .Select(line => line.Site)
            .ToList();

        Assert.True(waits.Count == 0, $"networkidle as readiness — use goto or settle: {string.Join("; ", waits)}");
    }

    [Fact]
    public void The_removed_readiness_waits_stay_removed()
    {
        var back = Lines(UiSuite().Concat(Harness()))
            .Where(line => RemovedReadinessWaits.Any(line.Text.Contains))
            .Select(line => line.Site)
            .ToList();

        Assert.True(back.Count == 0, $"A readiness wait the data-circuit marker replaced: {string.Join("; ", back)}");
    }

    [Fact]
    public void A_second_browser_comes_from_a_fixture()
    {
        var contexts = Lines(UiSuite())
            .Where(line => line.Text.Contains(".newContext(") && !ContextOwners.Contains(line.File))
            .Select(line => line.Site)
            .ToList();

        Assert.True(contexts.Count == 0, $"browser.newContext() in a spec — use the visitor or openPage fixture: {string.Join("; ", contexts)}");
    }

    [Fact]
    public void Both_harnesses_wait_for_the_same_readiness_marker()
    {
        var suite = Ready().Match(File.ReadAllText(Repository.PathTo("tests", "ui", "helpers.js")));
        var harness = Ready().Match(File.ReadAllText(Repository.PathTo("scripts", "blazor.mjs")));

        Assert.True(suite.Success && harness.Success, "READY not found in tests/ui/helpers.js or scripts/blazor.mjs");
        Assert.Equal(suite.Groups[1].Value, harness.Groups[1].Value);
        Assert.Contains("data-circuit", suite.Groups[1].Value);
    }

    [Fact]
    public void The_verify_matrix_ignores_the_same_console_noise_as_the_suite()
    {
        var suite = IgnoredPatterns(Repository.PathTo("tests", "ui", "fixtures.js"));
        var matrix = IgnoredPatterns(Repository.PathTo("scripts", "verify-matrix.mjs"));

        Assert.NotEmpty(suite);
        Assert.Equal(suite, matrix);
    }

    [Fact]
    public void Every_interactive_page_opens_with_the_readiness_marker()
    {
        // The marker fails open: a page without it has none left pending, so goto takes its inert prerender as ready.
        var pages = Repository.FilesUnder("src", ".razor").Where(path => File.ReadAllText(path).Contains("@rendermode")).ToList();
        var unmarked = pages.Where(path => !File.ReadAllText(path).Contains("<InteractiveShell ")).Select(Path.GetFileName).ToList();

        Assert.True(pages.Count > 5, $"Only {pages.Count} interactive pages found — has the scan stopped matching?");
        Assert.True(unmarked.Count == 0, $"Interactive pages without <InteractiveShell />: {string.Join(", ", unmarked)}");
    }

    [Fact]
    public void The_browser_jobs_install_from_the_lockfile_and_report_when_skipped()
    {
        var ci = File.ReadAllLines(Repository.PathTo(".github", "workflows", "ci.yml"));
        var gate = Array.FindIndex(ci, line => line.Trim() == "name: Playwright");

        Assert.True(gate >= 0, "No job named exactly Playwright — it is the required check in the ruleset");
        Assert.Contains(ci[gate..Math.Min(ci.Length, gate + 6)], line => line.Trim() == "if: always()");
        Assert.DoesNotContain(ci, line => line.Contains("npm install"));
        Assert.Contains(ci, line => line.Contains("hashFiles('tests/ui/package-lock.json')"));

        var ignored = Repository.FilesUnder("", ".gitignore")
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Contains("package-lock"))
            .ToList();
        Assert.Empty(ignored);
        Assert.True(File.Exists(Repository.PathTo("tests", "ui", "package-lock.json")) && File.Exists(Repository.PathTo("scripts", "package-lock.json")));
    }

    [Fact]
    public void The_scan_reads_the_suites_it_is_meant_to_judge()
    {
        // Guards the scan itself: one that read no spec would pass every rule above.
        var specs = UiSuite().Count(path => path.EndsWith(".spec.js"));
        var harness = Harness().Count();
        var polls = Lines(Harness()).Count(line => Sleep().IsMatch(line.Text));

        Assert.True(specs > 20 && harness > 3 && polls > 0, $"{specs} specs, {harness} harness scripts, {polls} polls — have they moved?");
    }

    private sealed record Line(string File, int Number, string Text)
    {
        public string Site => $"{File}:{Number}";
    }

    private static IEnumerable<string> UiSuite() => Repository.FilesUnder(Path.Combine("tests", "ui"), ".js");

    private static IEnumerable<string> Harness() => Repository.FilesUnder("scripts", ".mjs");

    private static IEnumerable<Line> Lines(IEnumerable<string> paths) =>
        paths.SelectMany(path => File.ReadLines(path).Select((text, index) => new Line(Path.GetFileName(path), index + 1, text)));

    private static List<string> IgnoredPatterns(string path) =>
        [.. RegexLiteral().Matches(IgnoredList().Match(File.ReadAllText(path)).Groups[1].Value).Select(match => match.Value).Order()];
}
