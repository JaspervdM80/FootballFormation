namespace FootballFormation.Core.Tests;

/// A password link is a credential in a URL, so what is pinned here is everything that limits one: who may make it, that only its hash is
/// kept, that it works once and not for long, and that the anonymous half tells a stranger nothing.
public class PasswordLinkServiceTests : ServiceTestBase
{
    private const string GoodPassword = "correct-horse";
    private const string NewPassword = "battery-staple";

    private readonly Team _team;

    public PasswordLinkServiceTests() => _team = SeedTeam();

    private async Task<AppUser> SeedCoachAsync(string? email = "coach@example.com") =>
        (await Users.CreateAsync("Coach", "coach", GoodPassword, UserRole.Admin, _team.Id, email)).Value!;

    [Fact]
    public async Task A_link_is_stored_only_as_its_hash()
    {
        var coach = await SeedCoachAsync();

        var token = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        var stored = Read().PasswordLinks.Single();
        Assert.Equal(coach.Id, stored.UserId);
        Assert.DoesNotContain(token, stored.TokenHash);
        Assert.Equal(Now + PasswordLinkService.CopiedLinkLifetime, stored.ExpiresUtc);
    }

    [Fact]
    public async Task A_link_sets_the_password_once_and_signs_out_every_session()
    {
        var coach = await SeedCoachAsync();
        var token = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        Assert.True((await PasswordLinks.ResetPasswordAsync(token, NewPassword)).IsSuccess);

        Assert.NotNull(await Users.ValidateCredentialsAsync("coach", NewPassword));
        Assert.Null(await Users.ValidateCredentialsAsync("coach", GoodPassword));
        Assert.Null(await Users.FindForSessionAsync(coach.Id, coach.SecurityStamp));
        Assert.Empty(Read().PasswordLinks);

        var again = await PasswordLinks.ResetPasswordAsync(token, "another-password");
        Assert.Equal(PasswordLinkService.UnusableLinkKey, again.ErrorKey);
        Assert.False((await PasswordLinks.IsUsableAsync(token)).Value);
    }

    [Fact]
    public async Task A_link_releases_the_seeded_password_gate()
    {
        await Users.EnsureAdminSeededAsync();
        var seeded = Read().Users.Single(u => u.Username == "admin");

        var token = (await PasswordLinks.CreateLinkAsync(seeded.Id)).Value!;
        await PasswordLinks.ResetPasswordAsync(token, NewPassword);

        Assert.False(Read().Users.Single(u => u.Id == seeded.Id).MustChangePassword);
    }

    [Fact]
    public async Task A_link_stops_working_when_it_expires()
    {
        var coach = await SeedCoachAsync();
        var token = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        Time.Advance(PasswordLinkService.CopiedLinkLifetime);

        Assert.False((await PasswordLinks.IsUsableAsync(token)).Value);
        Assert.Equal(PasswordLinkService.UnusableLinkKey, (await PasswordLinks.ResetPasswordAsync(token, NewPassword)).ErrorKey);
    }

    [Fact]
    public async Task A_password_changed_another_way_retires_the_link()
    {
        var coach = await SeedCoachAsync();
        var token = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        await Users.SetPasswordAsync(coach.Id, "set-by-an-admin");

        Assert.False((await PasswordLinks.IsUsableAsync(token)).Value);
    }

    [Fact]
    public async Task Only_the_latest_link_for_an_account_works()
    {
        var coach = await SeedCoachAsync();
        var first = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;
        var second = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        Assert.False((await PasswordLinks.IsUsableAsync(first)).Value);
        Assert.True((await PasswordLinks.IsUsableAsync(second)).Value);
    }

    [Fact]
    public async Task A_link_that_was_never_issued_opens_nothing()
    {
        Assert.False((await PasswordLinks.IsUsableAsync("made-up")).Value);
        Assert.False((await PasswordLinks.IsUsableAsync(null)).Value);
        Assert.Equal(PasswordLinkService.UnusableLinkKey, (await PasswordLinks.ResetPasswordAsync("made-up", NewPassword)).ErrorKey);
    }

    [Fact]
    public async Task A_short_password_is_refused_and_the_link_survives_it()
    {
        var coach = await SeedCoachAsync();
        var token = (await PasswordLinks.CreateLinkAsync(coach.Id)).Value!;

        var result = await PasswordLinks.ResetPasswordAsync(token, "short");

        Assert.Equal("Password must be at least {0} characters", result.ErrorKey);
        Assert.True((await PasswordLinks.IsUsableAsync(token)).Value);
    }

    [Fact]
    public async Task Making_a_link_is_refused_to_anyone_who_could_not_reset_the_password()
    {
        var coach = await SeedCoachAsync();
        var appAdmin = (await Users.CreateAsync("Owner", "owner", GoodPassword, UserRole.ApplicationAdmin)).Value!;
        var other = SeedTeam("Club B", "MO17-1");
        var theirs = (await Users.CreateAsync("Theirs", "theirs", GoodPassword, UserRole.Admin, other.Id)).Value!;
        SeedTeam();

        CurrentUser.IsApplicationAdmin = false;
        CurrentUser.AdminTeamId = _team.Id;

        Assert.True((await PasswordLinks.CreateLinkAsync(coach.Id)).IsSuccess);
        Assert.Equal("You can only manage accounts for your own team", (await PasswordLinks.CreateLinkAsync(theirs.Id)).ErrorKey);
        // An application admin is on no team, so an ordinary admin is refused one: choosing that password is taking over every team.
        Assert.Equal("You can only manage accounts for your own team", (await PasswordLinks.CreateLinkAsync(appAdmin.Id)).ErrorKey);

        CurrentUser.IsAdmin = false;
        Assert.True((await PasswordLinks.CreateLinkAsync(coach.Id)).IsFailure);
    }

    [Fact]
    public async Task A_reset_request_for_a_known_address_issues_a_short_lived_link()
    {
        await SeedCoachAsync();

        var mail = (await PasswordLinks.RequestResetAsync(" COACH@example.com ")).Value!;

        Assert.Equal("coach@example.com", mail.Email);
        Assert.Equal("coach", mail.Username);
        Assert.True((await PasswordLinks.IsUsableAsync(mail.Token)).Value);
        Assert.Equal(Now + PasswordLinkService.MailedLinkLifetime, Read().PasswordLinks.Single().ExpiresUtc);
    }

    [Fact]
    public async Task A_reset_request_needs_no_signed_in_user()
    {
        await SeedCoachAsync();
        CurrentUser.IsAdmin = false;
        CurrentUser.IsApplicationAdmin = false;

        var mail = (await PasswordLinks.RequestResetAsync("coach@example.com")).Value!;

        Assert.True((await PasswordLinks.ResetPasswordAsync(mail.Token, NewPassword)).IsSuccess);
    }

    [Fact]
    public async Task A_reset_request_for_an_unknown_or_malformed_address_sends_nothing()
    {
        await SeedCoachAsync();

        Assert.Null((await PasswordLinks.RequestResetAsync("stranger@example.com")).Value);
        Assert.Null((await PasswordLinks.RequestResetAsync("not an address")).Value);
        Assert.Null((await PasswordLinks.RequestResetAsync("")).Value);
        Assert.Empty(Read().PasswordLinks);
    }

    [Fact]
    public async Task A_second_reset_request_within_the_interval_sends_nothing()
    {
        await SeedCoachAsync();
        var first = (await PasswordLinks.RequestResetAsync("coach@example.com")).Value!;

        Time.Advance(PasswordLinkService.MailInterval - TimeSpan.FromSeconds(1));
        Assert.Null((await PasswordLinks.RequestResetAsync("coach@example.com")).Value);
        Assert.True((await PasswordLinks.IsUsableAsync(first.Token)).Value);

        Time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull((await PasswordLinks.RequestResetAsync("coach@example.com")).Value);
    }
}
