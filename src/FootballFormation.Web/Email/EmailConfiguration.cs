namespace FootballFormation.Web.Email;

/// Absent settings are an ordinary state, as with push: a developer's machine and CI have none, so nothing is mailed and the
/// forgotten-password link is not offered.
public sealed class EmailConfiguration(string? from, string? resendApiKey, Uri? publicUrl, string? pickupDirectory = null)
{
    public string? From { get; } = from;

    public string? ResendApiKey { get; } = resendApiKey;

    /// Where a mailed link points. Configured rather than read off the request, whose Host header the requester chooses — taking it
    /// from there would let anyone have a genuine reset mail carry a link to their own site.
    public Uri? PublicUrl { get; } = publicUrl;

    /// Development only: mail is written here as files instead of sent, which is how the browser tests read a reset link back.
    public string? PickupDirectory { get; } = pickupDirectory;

    public bool IsConfigured => From is not null && PublicUrl is not null && (ResendApiKey is not null || PickupDirectory is not null);

    /// Fly secrets in production: Email__From, Email__ResendApiKey and Email__PublicUrl.
    public static EmailConfiguration Read(IConfiguration configuration, bool isDevelopment)
    {
        Uri.TryCreate(configuration["Email:PublicUrl"], UriKind.Absolute, out var publicUrl);

        return new EmailConfiguration(
            Setting(configuration, "Email:From"),
            Setting(configuration, "Email:ResendApiKey"),
            publicUrl,
            isDevelopment ? Setting(configuration, "Email:PickupDirectory") : null);
    }

    private static string? Setting(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : null;
}
