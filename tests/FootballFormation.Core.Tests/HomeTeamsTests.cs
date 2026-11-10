using FootballFormation.Core.Security;

namespace FootballFormation.Core.Tests;

public class HomeTeamsTests
{
    private static HomeTeams From(params (string Host, string? Value)[] entries) =>
        new(entries.Select(e => KeyValuePair.Create(e.Host, e.Value)));

    [Fact]
    public void A_configured_domain_opens_on_its_team()
    {
        var homeTeams = From(("gjs-meiden.nl", "1"), ("voorbeeld.nl", "4"));

        Assert.Equal(1, homeTeams.For("gjs-meiden.nl"));
        Assert.Equal(4, homeTeams.For("voorbeeld.nl"));
    }

    /// Browsers lower-case the host, but a hand-written setting need not be.
    [Fact]
    public void A_domain_matches_whatever_its_case()
    {
        Assert.Equal(1, From(("GJS-Meiden.nl", "1")).For("gjs-meiden.nl"));
    }

    [Fact]
    public void A_domain_with_no_entry_has_no_home_team()
    {
        var homeTeams = From(("gjs-meiden.nl", "1"));

        Assert.Null(homeTeams.For("voetbal-team.nl"));
        Assert.Null(homeTeams.For("www.gjs-meiden.nl"));
        Assert.Null(From().For("gjs-meiden.nl"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gjs")]
    [InlineData("0")]
    [InlineData("-3")]
    public void An_entry_that_names_no_team_is_ignored(string? value)
    {
        Assert.Null(From(("gjs-meiden.nl", value)).For("gjs-meiden.nl"));
    }
}
