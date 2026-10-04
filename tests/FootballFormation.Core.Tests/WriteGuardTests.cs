using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FootballFormation.Core.Tests;

/// <see cref="AuthorizationTests"/> pins the write methods someone remembered to list; this reads the source, so a new write that
/// reaches the database outside the admin guard fails the day it is written.
public class WriteGuardTests
{
    private static readonly HashSet<string> Writes =
        ["SaveChanges", "SaveChangesAsync", "ExecuteDeleteAsync", "ExecuteUpdateAsync", "ExecuteSqlRawAsync", "ExecuteSqlAsync"];

    private static readonly HashSet<string> Guards = ["RunAdminAsync", "RunApplicationAdminAsync"];

    private static readonly Dictionary<string, string> UnguardedByDesign = new()
    {
        ["PushSubscriptionService.SubscribeAsync"] = "the one anonymous write: nobody signs in to follow a match",
        ["PushSubscriptionService.UnsubscribeAsync"] = "the one anonymous write: nobody signs in to follow a match",
        ["PushSubscriptionService.RenewAsync"] = "the one anonymous write: nobody signs in to follow a match",
        ["MatchAudienceQuery.DropAsync"] = "the push sender pruning endpoints a push service called gone; there is no request or user",
        ["MatchPreferencesService.GetAsync"] = "creates the season's default row on first read; the unique index absorbs two at once",
        ["SeasonService.CloseGapsInAsync"] = "a boot repair, run before anyone can sign in",
        ["SeasonService.EnsureCurrentInAsync"] = "a boot step, run before anyone can sign in",
        ["TeamService.EnsureSeededAsync"] = "a boot step, run before anyone can sign in",
        ["TeamService.EnsureAdminsHaveTeamAsync"] = "a boot step, run before anyone can sign in",
        ["UserService.EnsureAdminSeededAsync"] = "a boot step, run before anyone can sign in",
        ["UserService.VerifyAsync"] = "sign-in rehashing an outdated password, before there is anyone to authorize",
        ["UserService.ChangePasswordAsync"] = "an account changing its own password against the current one; on the seeded one it is not an admin yet",
    };

    [Fact]
    public void Every_write_in_a_service_runs_inside_the_admin_guard()
    {
        var unguarded = UnguardedWrites().Where(site => !UnguardedByDesign.ContainsKey(site)).Distinct().ToList();

        Assert.True(unguarded.Count == 0,
            $"Writes outside RunAdminAsync — guard them, or exempt them below with the reason: {string.Join(", ", unguarded)}");
    }

    [Fact]
    public void Every_exemption_still_names_an_unguarded_write()
    {
        var sites = UnguardedWrites().ToHashSet();
        var stale = UnguardedByDesign.Keys.Where(site => !sites.Contains(site)).ToList();

        Assert.Empty(stale);
    }

    [Fact]
    public void The_scan_finds_the_writes_it_is_meant_to_judge()
    {
        // Guards the scan itself: one that matched no write would pass with every guard missing.
        var all = Repository.FilesUnder(Path.Combine("src", "FootballFormation.Core", "Services"), ".cs")
            .SelectMany(path => Parse(path).DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Count(call => Writes.Contains(NameOf(call)));

        Assert.True(all > 50, $"Only {all} writes found — has the scan stopped matching?");
    }

    private static IEnumerable<string> UnguardedWrites()
    {
        var calls = Repository.FilesUnder(Path.Combine("src", "FootballFormation.Core", "Services"), ".cs")
            .SelectMany(path => Parse(path).DescendantNodes().OfType<InvocationExpressionSyntax>())
            .ToList();

        return calls
            .Where(call => Writes.Contains(NameOf(call)))
            .Where(call => !InsideGuard(call) && !OnlyCalledFromGuards(call, calls))
            .Select(SiteOf);
    }

    /// A helper writing on its caller's context (RecountScorelineAsync) is guarded when every call to it is — so a new unguarded
    /// caller still fails, where exempting the helper by name would let it through.
    private static bool OnlyCalledFromGuards(SyntaxNode write, List<InvocationExpressionSyntax> calls)
    {
        if (write.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } helper) return false;

        var callers = calls.Where(call => NameOf(call) == helper.Identifier.Text).ToList();
        return callers.Count > 0 && callers.All(InsideGuard);
    }

    private static SyntaxNode Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

    private static bool InsideGuard(SyntaxNode node) =>
        node.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Any(lambda => lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax guard }
                && Guards.Contains(NameOf(guard)));

    private static string NameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax name => name.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        _ => ""
    };

    private static string SiteOf(SyntaxNode node)
    {
        var type = node.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.Text;
        var member = node.Ancestors().OfType<MemberDeclarationSyntax>().First() switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            var other => other.Kind().ToString()
        };
        return $"{type}.{member}";
    }
}
