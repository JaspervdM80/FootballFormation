using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// Accounts and the credential check. The rules worth pinning down are the ones that lock people
/// out when they go wrong: the last admin must survive, and anything that changes an account's
/// authority must invalidate the sessions that were opened under the old one.
/// </summary>
public class UserServiceTests : ServiceTestBase
{
    private const string GoodPassword = "correct-horse";

    [Fact]
    public async Task A_created_user_can_sign_in_with_their_password_but_not_a_wrong_one()
    {
        await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        Assert.NotNull(await Users.ValidateCredentialsAsync("jasper", GoodPassword));
        Assert.Null(await Users.ValidateCredentialsAsync("jasper", "wrong"));
        Assert.Null(await Users.ValidateCredentialsAsync("nobody", GoodPassword));
    }

    [Fact]
    public async Task The_role_claim_value_matches_the_string_Authorize_checks()
    {
        var created = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        // Program.cs mints ClaimTypes.Role from Role.ToString(); AppRoles.Admin is what the
        // [Authorize] attributes and AuthorizeView markup match against. If these ever differ,
        // every admin gate in the app silently stops letting anyone through.
        Assert.Equal(Core.Security.AppRoles.Admin, created.Value!.Role.ToString());
    }

    [Fact]
    public async Task A_password_is_stored_hashed_never_in_the_clear()
    {
        await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        await using var db = Read();
        var stored = await db.Users.FirstAsync(u => u.Username == "jasper");

        Assert.NotEqual(GoodPassword, stored.PasswordHash);
        Assert.DoesNotContain(GoodPassword, stored.PasswordHash);
    }

    [Fact]
    public async Task A_username_cannot_be_taken_twice()
    {
        await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        var duplicate = await Users.CreateAsync("Someone Else", "jasper", GoodPassword, UserRole.Admin);

        Assert.True(duplicate.IsFailure);
        Assert.Contains("already exists", duplicate.Error);
    }

    [Fact]
    public async Task A_username_cannot_be_renamed_onto_another_account()
    {
        var first = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);
        await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin);

        var clash = await Users.UpdateAsync(first.Value!.Id, "Jasper", "coach", UserRole.Admin);

        Assert.True(clash.IsFailure);
    }

    [Fact]
    public async Task A_password_shorter_than_the_minimum_is_refused()
    {
        var tooShort = await Users.CreateAsync("Jasper", "jasper", "short", UserRole.Admin);

        Assert.True(tooShort.IsFailure);
        Assert.Empty(await Read().Users.ToListAsync());
    }

    [Fact]
    public async Task The_last_admin_cannot_be_deleted()
    {
        await Users.EnsureAdminSeededAsync();
        var admin = (await Users.GetAllAsync()).Value!.Single();

        var deleted = await Users.DeleteAsync(admin.Id);

        Assert.True(deleted.IsFailure);
        Assert.Single(await Read().Users.ToListAsync());
    }

    [Fact]
    public async Task An_admin_can_be_deleted_once_another_admin_exists()
    {
        await Users.EnsureAdminSeededAsync();
        var seeded = (await Users.GetAllAsync()).Value!.Single();
        await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        var deleted = await Users.DeleteAsync(seeded.Id);

        Assert.True(deleted.IsSuccess);
        Assert.Single(await Read().Users.ToListAsync());
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_never_overwrites_a_changed_password()
    {
        await Users.EnsureAdminSeededAsync();
        Assert.Equal(PasswordChangeOk,
            await Users.ChangePasswordAsync("admin", "admin", GoodPassword));

        // Every startup calls this. It must not undo the password the user just set.
        await Users.EnsureAdminSeededAsync();

        Assert.Single(await Read().Users.ToListAsync());
        Assert.NotNull(await Users.ValidateCredentialsAsync("admin", GoodPassword));
        Assert.Null(await Users.ValidateCredentialsAsync("admin", "admin"));
    }

    [Fact]
    public async Task Resetting_a_password_replaces_the_old_one()
    {
        var created = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);

        Assert.True((await Users.SetPasswordAsync(created.Value!.Id, "brand-new-one")).IsSuccess);

        Assert.Null(await Users.ValidateCredentialsAsync("jasper", GoodPassword));
        Assert.NotNull(await Users.ValidateCredentialsAsync("jasper", "brand-new-one"));
    }

    [Fact]
    public async Task A_password_change_invalidates_a_session_opened_before_it()
    {
        var created = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);
        var user = created.Value!;

        // What the cookie carries at sign-in.
        var stampAtLogin = user.SecurityStamp;
        Assert.NotNull(await Users.FindForSessionAsync(user.Id, stampAtLogin));

        await Users.SetPasswordAsync(user.Id, "brand-new-one");

        Assert.Null(await Users.FindForSessionAsync(user.Id, stampAtLogin));
    }

    [Fact]
    public async Task A_deleted_user_has_no_valid_session_left()
    {
        await Users.EnsureAdminSeededAsync();
        var created = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);
        var user = created.Value!;

        await Users.DeleteAsync(user.Id);

        Assert.Null(await Users.FindForSessionAsync(user.Id, user.SecurityStamp));
    }

    [Fact]
    public async Task Renaming_a_user_leaves_their_session_alone()
    {
        var created = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);
        var user = created.Value!;

        await Users.UpdateAsync(user.Id, "Jasper van der Meijden", "jasper", UserRole.Admin);

        // Nothing about their authority changed, so signing them out would be gratuitous.
        Assert.NotNull(await Users.FindForSessionAsync(user.Id, user.SecurityStamp));
    }

    [Fact]
    public async Task Every_account_gets_its_own_security_stamp()
    {
        var first = await Users.CreateAsync("Jasper", "jasper", GoodPassword, UserRole.Admin);
        var second = await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin);

        // A shared stamp would mean one password change signed everybody out.
        Assert.NotEqual(first.Value!.SecurityStamp, second.Value!.SecurityStamp);
        Assert.NotEmpty(first.Value.SecurityStamp);
    }

    [Fact]
    public async Task Names_and_usernames_are_trimmed_so_a_stray_space_is_not_a_different_login()
    {
        await Users.CreateAsync("  Jasper  ", "  jasper  ", GoodPassword, UserRole.Admin);

        await using var db = Read();
        var stored = await db.Users.SingleAsync();

        Assert.Equal("Jasper", stored.DisplayName);
        Assert.Equal("jasper", stored.Username);
        Assert.NotNull(await Users.ValidateCredentialsAsync("jasper", GoodPassword));
    }

    [Fact]
    public async Task A_blank_name_or_username_is_refused()
    {
        Assert.True((await Users.CreateAsync("", "jasper", GoodPassword, UserRole.Admin)).IsFailure);
        Assert.True((await Users.CreateAsync("Jasper", "   ", GoodPassword, UserRole.Admin)).IsFailure);
        Assert.Empty(await Read().Users.ToListAsync());
    }

    [Fact]
    public async Task Users_are_listed_by_name()
    {
        await Users.CreateAsync("Zoe", "zoe", GoodPassword, UserRole.Admin);
        await Users.CreateAsync("Anna", "anna", GoodPassword, UserRole.Admin);
        await Users.CreateAsync("Mila", "mila", GoodPassword, UserRole.Admin);

        var all = await Users.GetAllAsync();

        Assert.Equal(["Anna", "Mila", "Zoe"], all.Value!.Select(u => u.DisplayName));
    }

    private const Core.Services.UserService.PasswordChangeResult PasswordChangeOk =
        Core.Services.UserService.PasswordChangeResult.Success;
}
