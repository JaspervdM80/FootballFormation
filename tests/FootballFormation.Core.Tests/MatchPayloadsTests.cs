using System.Globalization;
using System.Text.Json;
using FootballFormation.Core.Push;

namespace FootballFormation.Core.Tests;

public class MatchPayloadsTests
{
    private static (string, string) Compose() => ("Doelpunt", "GJS 1 - 0 Sliedrecht");

    /// The bug this exists to stop: IStringLocalizer reads the ambient culture, so a follower who subscribed in English would be sent
    /// Dutch because the last touchline write happened on a Dutch circuit.
    [Fact]
    public void Each_language_is_composed_with_that_language_in_scope()
    {
        var seen = new List<string>();

        MatchPayloads.For(["nl", "en"], "/games/1/live", 1, () =>
        {
            seen.Add(CultureInfo.CurrentUICulture.Name);
            return Compose();
        });

        Assert.Equal(["nl", "en"], seen);
    }

    [Fact]
    public void The_culture_that_was_in_scope_is_put_back()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("nl");

        MatchPayloads.For(["en"], "/games/1/live", 1, Compose);

        Assert.Equal("nl", CultureInfo.CurrentUICulture.Name);
    }

    /// Even when composing throws — otherwise one bad message leaves every later render on the wrong language.
    [Fact]
    public void The_culture_is_put_back_even_when_composing_fails()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("nl");

        Assert.Throws<InvalidOperationException>(() =>
            MatchPayloads.For(["en"], "/games/1/live", 1, () => throw new InvalidOperationException()));

        Assert.Equal("nl", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Two_followers_in_one_language_share_a_payload()
    {
        var composed = 0;

        var payloads = MatchPayloads.For(["nl", "nl", "nl"], "/games/1/live", 1, () =>
        {
            composed++;
            return Compose();
        });

        Assert.Equal(1, composed);
        Assert.Single(payloads);
    }

    [Fact]
    public void A_payload_carries_what_the_service_worker_reads_off_it()
    {
        var payloads = MatchPayloads.For(["nl"], "/games/7/live", 7, Compose);

        var message = JsonDocument.Parse(payloads["nl"]).RootElement;

        Assert.Equal("Doelpunt", message.GetProperty("title").GetString());
        Assert.Equal("GJS 1 - 0 Sliedrecht", message.GetProperty("body").GetString());
        Assert.Equal("/games/7/live", message.GetProperty("url").GetString());
        // One notification per match, so a third goal replaces the second rather than stacking.
        Assert.Equal("match-7", message.GetProperty("tag").GetString());
    }
}
