using FootballFormation.Core.Reporting;

namespace FootballFormation.Core.Tests;

public class HealthReportTests
{
    private const string Sha = "d5ba72bb10ada2aa04ef454a7c4a15c5de691da3";

    [Fact]
    public void A_migrated_database_is_healthy()
    {
        var status = HealthReport.Build(Sha, applied: 17, pending: 0);

        Assert.True(status.IsHealthy);
        Assert.Equal(HealthStatus.Healthy, status.Status);
        Assert.Null(status.Detail);
    }

    [Fact]
    public void The_commit_is_reported_back_so_a_deploy_can_recognise_its_own_release()
    {
        var status = HealthReport.Build(Sha, applied: 17, pending: 0);

        Assert.Equal(Sha, status.Version);
    }

    [Fact]
    public void A_migration_still_pending_after_startup_is_unhealthy()
    {
        // The app migrates itself on boot, so anything outstanding by the time it serves means the
        // boot did not finish — a half-applied schema serving pages is the case this refuses.
        var status = HealthReport.Build(Sha, applied: 16, pending: 1);

        Assert.False(status.IsHealthy);
        Assert.Equal(HealthStatus.Unhealthy, status.Status);
        Assert.Contains("1 migration(s) still pending", status.Detail);
    }

    [Fact]
    public void The_counts_survive_onto_an_unhealthy_report()
    {
        // The numbers are what says *how far* a stuck migration got, so they have to be reported
        // on the failure and not only on the success.
        var status = HealthReport.Build(Sha, applied: 16, pending: 2);

        Assert.Equal(16, status.AppliedMigrations);
        Assert.Equal(2, status.PendingMigrations);
    }

    [Fact]
    public void An_unreachable_database_is_unhealthy_and_carries_the_reason()
    {
        var status = HealthReport.Unreachable(Sha, "unable to open database file");

        Assert.False(status.IsHealthy);
        Assert.Equal("unable to open database file", status.Detail);
        Assert.Equal(Sha, status.Version);
    }

    [Fact]
    public void An_unreachable_database_does_not_claim_a_migration_count()
    {
        // Zero here means "not known", and it must not be mistaken for a real reading — a report
        // of "0 applied, 0 pending" alongside healthy would describe a fresh install.
        var status = HealthReport.Unreachable(Sha, "unable to open database file");

        Assert.Equal(0, status.AppliedMigrations);
        Assert.False(status.IsHealthy);
    }
}
