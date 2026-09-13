using System.Globalization;
using System.Text.Json;

namespace FootballFormation.Core.Push;

/// Here rather than beside the sender for the same reason <see cref="PushDeliveryOutcome"/> is: Web carries no unit tests, and a
/// follower who subscribed in English being sent Dutch is invisible until someone complains in the wrong language.
public static class MatchPayloads
{
    /// One payload per language, not per follower. <paramref name="compose"/> is called with the culture already swapped in, because
    /// IStringLocalizer reads the ambient one rather than taking it as an argument.
    public static Dictionary<string, byte[]> For(
        IEnumerable<string> cultures, string url, int gameId, Func<(string Title, string Body)> compose)
    {
        var payloads = new Dictionary<string, byte[]>();

        foreach (var culture in cultures.Distinct())
        {
            var previous = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            try
            {
                var (title, body) = compose();

                payloads[culture] = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    title,
                    body,
                    url,
                    // One notification per match rather than a stack of them: the newest score replaces the one before it.
                    tag = $"match-{gameId}"
                });
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        return payloads;
    }
}
