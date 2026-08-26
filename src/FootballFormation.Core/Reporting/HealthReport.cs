using System.Text.Json.Serialization;

namespace FootballFormation.Core.Reporting;

/// <summary>
/// What <c>/health</c> answers with. Serialised straight to JSON, so the property names are the
/// wire format the deploy workflow reads — renaming one is a breaking change to the smoke check.
/// </summary>
public record HealthStatus(string Status, string Version, int AppliedMigrations, int PendingMigrations, string? Detail)
{
    [JsonIgnore]
    public bool IsHealthy => Status == Healthy;

    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";
}

/// <summary>
/// Decides whether a running container is actually serving correctly, from facts the endpoint
/// gathers. Pure, and here rather than in <c>Program.cs</c>, so the rules that decide "unhealthy"
/// are pinned by tests instead of only ever exercised against production.
/// </summary>
public static class HealthReport
{
    /// <param name="version">The commit the container was built from.</param>
    /// <param name="applied">Migrations already on the database.</param>
    /// <param name="pending">Migrations still outstanding.</param>
    public static HealthStatus Build(string version, int applied, int pending)
    {
        // Pending migrations *after* boot mean the boot did not finish its job — this app migrates
        // itself on startup, so by the time it serves there should be none left. Serving in that
        // state is how a half-applied schema reaches a parent looking up a line-up: the pages that
        // touch untouched tables work, and the ones that do not fail strangely. Better to fail the
        // deploy.
        if (pending > 0)
        {
            return new HealthStatus(HealthStatus.Unhealthy, version, applied, pending, $"{pending} migration(s) still pending after startup");
        }

        return new HealthStatus(HealthStatus.Healthy, version, applied, pending, null);
    }

    /// <summary>
    /// The database could not be read at all. Kept separate from <see cref="Build"/> because there
    /// is no migration count to report — not "zero migrations", which would be a lie that reads as
    /// a fresh install.
    /// </summary>
    public static HealthStatus Unreachable(string version, string detail) => new(HealthStatus.Unhealthy, version, 0, 0, detail);
}
