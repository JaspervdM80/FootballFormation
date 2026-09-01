namespace FootballFormation.UI.State;

/// A plain record rather than an <c>IHttpContextAccessor</c> injection: this RCL takes no ASP.NET Core hosting dependency, which is what
/// leaves the door open to a MAUI Blazor Hybrid host. All three are cookies, so a circuit's scope — created by /_blazor — has them too.
public sealed record RequestContext(string? SeasonCookie, string? TrailCookie, string? TeamCookie)
{
    /// A scope with no request behind it, which therefore knows nothing.
    public static readonly RequestContext None = new(null, null, null);
}
