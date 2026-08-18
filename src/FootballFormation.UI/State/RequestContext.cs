namespace FootballFormation.UI.State;

/// <summary>
/// What the HTTP request knew, captured once per DI scope so the components rendered in that scope
/// can read it without reaching for <c>HttpContext</c> themselves.
/// <para>
/// A plain record rather than something injecting <c>IHttpContextAccessor</c>: this project is a
/// Razor Class Library with no ASP.NET Core hosting dependency, and keeping it that way is what
/// leaves the door open to a MAUI Blazor Hybrid host. The web host fills it in — see
/// <c>Program.cs</c>.
/// </para>
/// </summary>
/// <param name="SeasonCookie">The raw <c>ff.season</c> value, for <see cref="SeasonState"/>.</param>
/// <param name="Referer">
/// The referring URL, for <c>NavigationTrail</c>. Empty in a circuit's scope: that scope is created
/// during the <c>/_blazor</c> request, which carries cookies but no referrer.
/// </param>
public sealed record RequestContext(string? SeasonCookie, string? Referer)
{
    /// <summary>A scope with no request behind it, which therefore knows nothing.</summary>
    public static readonly RequestContext None = new(null, null);
}
