using FootballFormation.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// The address is what a forgotten password is recovered through, so it has to be unreadable at rest, unambiguous, and harmless when the
/// key ring that wrote it is not the one reading it.
public class UserEmailTests : ServiceTestBase
{
    private const string GoodPassword = "correct-horse";

    public UserEmailTests() => SeedTeam();

    [Fact]
    public async Task An_email_is_stored_encrypted_and_read_back_normalised()
    {
        await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin, email: "  Coach@Example.COM ");

        var stored = Read().Users.Single();
        Assert.NotNull(stored.ProtectedEmail);
        Assert.DoesNotContain("coach@example.com", stored.ProtectedEmail, StringComparison.OrdinalIgnoreCase);

        var listed = (await Users.GetAllAsync()).Value!.Single();
        Assert.Equal("coach@example.com", listed.Email);
    }

    [Fact]
    public async Task An_invalid_email_is_refused()
    {
        var result = await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin, email: "not-an-address");

        Assert.Equal("That is not a valid email address", result.ErrorKey);
        Assert.Empty(Read().Users);
    }

    [Fact]
    public async Task Two_accounts_cannot_share_an_email_however_it_is_typed()
    {
        await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin, email: "coach@example.com");
        var assistant = (await Users.CreateAsync("Assistant", "assistant", GoodPassword, UserRole.Admin)).Value!;

        var created = await Users.CreateAsync("Other", "other", GoodPassword, UserRole.Admin, email: "COACH@example.com");
        var updated = await Users.UpdateAsync(assistant.Id, "Assistant", "assistant", UserRole.Admin, email: "coach@example.com");

        Assert.Equal("Another account already uses that email address", created.ErrorKey);
        Assert.Equal("Another account already uses that email address", updated.ErrorKey);
    }

    [Fact]
    public async Task Saving_an_account_keeps_its_own_email_and_an_empty_one_clears_it()
    {
        var coach = (await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin, email: "coach@example.com")).Value!;
        var ciphertext = Read().Users.Single().ProtectedEmail;

        Assert.True((await Users.UpdateAsync(coach.Id, "Coach Renamed", "coach", UserRole.Admin, email: "coach@example.com")).IsSuccess);
        Assert.Equal(ciphertext, Read().Users.Single().ProtectedEmail);

        Assert.True((await Users.UpdateAsync(coach.Id, "Coach Renamed", "coach", UserRole.Admin, email: " ")).IsSuccess);
        Assert.Null(Read().Users.Single().ProtectedEmail);
    }

    [Fact]
    public async Task An_email_another_key_ring_wrote_reads_as_none()
    {
        // A live database copied to a laptop: the rows are there, the key that wrote them is not.
        var foreign = new EmailProtector(new FakeDataProtectionProvider(), NullLogger<EmailProtector>.Instance);
        var coach = (await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin)).Value!;
        await Db.Users.Where(u => u.Id == coach.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ProtectedEmail, foreign.Protect("coach@example.com")));

        Assert.Null((await Users.GetAllAsync()).Value!.Single().Email);
        Assert.Null(EmailProtector.Unprotect("not even base64url!"));
    }

    [Fact]
    public async Task An_account_created_without_a_password_cannot_sign_in()
    {
        var created = await Users.CreateAsync("Coach", "coach", null, UserRole.Admin);

        Assert.True(created.IsSuccess);
        Assert.Null(await Users.ValidateCredentialsAsync("coach", ""));
        Assert.False(string.IsNullOrEmpty(Read().Users.Single().PasswordHash));
    }
}
