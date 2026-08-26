namespace FootballFormation.UI.State;

/// A plain record rather than an <c>IHttpContextAccessor</c> injection: this RCL takes no ASP.NET Core hosting dependency, which is what
/// leaves the door open to a MAUI Blazor Hybrid host. <paramref name="Referer"/> is empty in a circuit's scope, created by /_blazor.
public sealed record RequestContext(string? SeasonCookie, string? Referer)
{
    /// A scope with no request behind it, which therefore knows nothing.
    public static readonly RequestContext None = new(null, null);
}
