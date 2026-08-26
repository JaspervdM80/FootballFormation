namespace FootballFormation.Core.Tests;

/// The seeded password is in the source and in the log, so what keeps a new deployment from being an open door is the flag holding the
/// account back until it is replaced — not secrecy.
public class SeededAdminTests : ServiceTestBase
{
    [Fact]
    public async Task A_fresh_install_seeds_an_admin_that_must_change_its_password()
    {
        await Users.EnsureAdminSeededAsync();

        var admin = Read().Users.Single();
        Assert.Equal("admin", admin.Username);
        Assert.True(admin.MustChangePassword);
    }

    [Fact]
    public async Task Seeding_does_nothing_once_any_account_exists()
    {
        await Users.CreateAsync("Jasper", "jasper", "correct-horse", Core.Models.UserRole.Admin);

        await Users.EnsureAdminSeededAsync();

        // Not just "no second row": running the seeder over a live database must never resurrect
        // the default login, which is what an unconditional insert would do.
        Assert.Single(Read().Users);
        Assert.Null(await Users.ValidateCredentialsAsync("admin", "admin"));
    }

    [Fact]
    public async Task Changing_the_password_releases_the_gate()
    {
        await Users.EnsureAdminSeededAsync();

        var result = await Users.ChangePasswordAsync("admin", "admin", "correct-horse");

        Assert.Equal(Core.Services.UserService.PasswordChangeResult.Success, result);
        Assert.False(Read().Users.Single().MustChangePassword);
    }

    [Fact]
    public async Task Changing_the_password_invalidates_the_gated_session()
    {
        await Users.EnsureAdminSeededAsync();
        var stampAtLogin = Read().Users.Single().SecurityStamp;
        var id = Read().Users.Single().Id;

        await Users.ChangePasswordAsync("admin", "admin", "correct-horse");

        // The cookie minted at sign-in carries the MustChangePassword claim. Rolling the stamp is
        // what stops that stale claim outliving the condition — the next request re-authenticates.
        Assert.Null(await Users.FindForSessionAsync(id, stampAtLogin));
    }

    [Fact]
    public async Task The_seeded_password_still_has_to_be_the_real_one()
    {
        await Users.EnsureAdminSeededAsync();

        Assert.NotNull(await Users.ValidateCredentialsAsync("admin", "admin"));
        Assert.Null(await Users.ValidateCredentialsAsync("admin", "wrong"));
    }
}
