using System.Net.Http.Headers;

namespace FootballFormation.Web.Email;

/// https://resend.com/docs/api-reference/emails/send-email
public sealed class ResendEmailSender(
    IHttpClientFactory httpClientFactory, EmailConfiguration configuration, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public const string HttpClientName = "Resend";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = configuration.From,
                to = new[] { message.To },
                subject = message.Subject,
                text = message.Text,
                html = message.Html
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ResendApiKey);

        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);

        // The status alone: Resend's error body can echo the recipient back.
        if (!response.IsSuccessStatusCode)
            logger.LogError("Resend refused a mail with status {Status}", (int)response.StatusCode);
    }
}
