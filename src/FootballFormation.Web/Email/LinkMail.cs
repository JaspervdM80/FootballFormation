using System.Net;

namespace FootballFormation.Web.Email;

/// The one layout every mail carrying a password link shares, so the encoding lives in one place.
public static class LinkMail
{
    public static EmailMessage Compose(string to, string action, string link, string[] before, string[] after)
    {
        var text = string.Join("\n\n", [.. before, $"{action}: {link}", .. after]) + "\n";

        var html = string.Join("\n", [
            .. before.Select(Paragraph),
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>",
            .. after.Select(Paragraph)]);

        return new EmailMessage(to, action, text, html);
    }

    private static string Paragraph(string line) => $"<p>{WebUtility.HtmlEncode(line)}</p>";
}
