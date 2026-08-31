namespace FootballFormation.Web.ServiceExtensions;

/// The caller's address for the login rate limiter and audit log. fly-proxy sets Fly-Client-IP to the real client and overwrites any the
/// client sent, so it holds where a hand-rolled X-Forwarded-For does not; RemoteIpAddress is the fallback for a local run off Fly.
public static class ClientIp
{
    public static string Of(HttpContext context) =>
        context.Request.Headers["Fly-Client-IP"].FirstOrDefault() is { Length: > 0 } fly
            ? fly
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
