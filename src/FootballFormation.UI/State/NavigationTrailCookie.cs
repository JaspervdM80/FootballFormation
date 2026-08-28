namespace FootballFormation.UI.State;

/// The pages the visitor has just been on, newest first. A cookie rather than the <c>Referer</c> header, which under Blazor's enhanced
/// navigation names the page being loaded rather than the one being left — see docs/known_issues/blazor-components.md.
public static class NavigationTrailCookie
{
    public const string CookieName = "ff.trail";

    /// Two, because that is what the circuit's own scope needs: its /_blazor request carries the cookie the page already wrote, so the
    /// page it came from is the second entry. One more would only be read after two pages the route table cannot name.
    private const int Depth = 2;

    /// A hand-edited cookie is not worth an error, so anything that is not an in-app path is simply dropped.
    public static IReadOnlyList<string> Parse(string? cookieValue) =>
        string.IsNullOrEmpty(cookieValue)
            ? []
            : [.. cookieValue.Split('|', StringSplitOptions.RemoveEmptyEntries).Where(IsAppPath).Take(Depth)];

    /// <paramref name="path"/> pushed onto the front of <paramref name="trail"/>. A refresh finds itself already there and changes nothing.
    public static string Format(string path, IReadOnlyList<string> trail) =>
        string.Join('|', trail.Where(entry => entry != path).Prepend(path).Take(Depth));

    /// Rooted and single-slashed: "//elsewhere.example" is a URL the browser would leave the site for.
    private static bool IsAppPath(string path) => path.StartsWith('/') && !path.StartsWith("//");
}
