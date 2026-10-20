namespace FootballFormation.Core.Tests;

public class LinkSlugTests
{
    [Theory]
    [InlineData("GJS", "gjs")]
    [InlineData("MO15-2", "mo15-2")]
    [InlineData("SV Zwaluwen", "sv_zwaluwen")]
    [InlineData("Réal Café", "real_cafe")]
    [InlineData("VV 't Zand", "vv_t_zand")]
    [InlineData("S.V. Oranje-Wit '04", "s_v_oranje-wit_04")]
    [InlineData("  Ñandú  ", "nandu")]
    [InlineData("MO15 (meiden)", "mo15_meiden")]
    public void A_name_keeps_its_letters_in_lowercase_its_digits_and_hyphens_and_turns_the_rest_into_one_underscore(string name, string expected)
    {
        Assert.Equal(expected, LinkSlug.Of(name));
    }

    /// An empty segment would make /team/{club}/{team} a route nothing matches.
    [Fact]
    public void A_name_with_nothing_to_keep_still_has_a_slug()
    {
        Assert.Equal("_", LinkSlug.Of("!!!"));
    }

    [Fact]
    public void A_club_and_a_team_each_name_their_part_of_the_link()
    {
        var team = new Team { Name = "MO15 Één", Club = new Club { Name = "GJS Meiden" } };

        Assert.Equal("gjs_meiden", team.Club.Slug);
        Assert.Equal("mo15_een", team.Slug);
    }

    [Fact]
    public void A_slug_is_its_own_slug_so_a_link_reads_back_to_its_name()
    {
        Assert.True(LinkSlug.SameLink("SV Zwaluwen", "SV_Zwaluwen"));
        Assert.True(LinkSlug.SameLink("Réal Café", "REAL_CAFE"));
        Assert.False(LinkSlug.SameLink("MO15-2", "MO15_2"));
    }
}
