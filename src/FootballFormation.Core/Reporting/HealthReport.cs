using System.Text.Json.Serialization;

namespace FootballFormation.Core.Reporting;

/// Serialised straight to JSON, so these property names are the wire format the deploy workflow reads — renaming one breaks the smoke check.
public record HealthStatus(string Status, string Version, int AppliedMigrations, int PendingMigrations, string? Detail)
{
    [JsonIgnore]
    public bool IsHealthy => Status == Healthy;

    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";
}

/// Here rather than in Program.cs so the rules that decide "unhealthy" are pinned by tests instead of only ever exercised in production.
public static class HealthReport
{

    public static HealthStatus Build(string version, int applied, int pending)
    {
        // This app migrates itself on startup, so a pending migration by the time it serves means the boot did not finish — and a
        // half-applied schema fails strangely on exactly the pages that touch the changed tables. Better to fail the deploy.
        if (pending > 0)
        {
            return new HealthStatus(HealthStatus.Unhealthy, version, applied, pending, $"{pending} migration(s) still pending after startup");
        }

        return new HealthStatus(HealthStatus.Healthy, version, applied, pending, null);
    }

    /// Separate from <see cref="Build"/> because there is no migration count to report — not "zero", which would read as a fresh install.
    public static HealthStatus Unreachable(string version, string detail) => new(HealthStatus.Unhealthy, version, 0, 0, detail);
}
