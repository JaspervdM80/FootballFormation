using System.Text.Json;

namespace FootballFormation.Web.Email;

/// Writes each mail to <see cref="EmailConfiguration.PickupDirectory"/> as JSON instead of sending it. Development only.
public sealed class PickupDirectoryEmailSender(EmailConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateDirectory(configuration.PickupDirectory!);
        var path = Path.Combine(directory.FullName, $"{Guid.NewGuid():N}.json");

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(message), cancellationToken);
    }
}
