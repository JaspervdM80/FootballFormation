namespace FootballFormation.Core.Tests;

public class FormationTypeTests
{
    public static TheoryData<FormationType> AllFormations() => [.. Enum.GetValues<FormationType>()];

    [Theory]
    [MemberData(nameof(AllFormations))]
    public void Every_formation_fields_one_short_of_its_format(FormationType formation)
    {
        // The pitch builds its slots as [GK, ..DefaultPositions()], and Format() reads the format back off that count — so a shape with
        // the wrong number of outfielders lands in a format nothing offers and disappears from every picker.
        Assert.Contains(formation.Format(), Enum.GetValues<MatchFormat>());
        Assert.Equal(formation.Format().PlayerCount() - 1, formation.DefaultPositions().Length);
    }

    [Theory]
    [MemberData(nameof(AllFormations))]
    public void No_formation_puts_a_keeper_in_an_outfield_slot(FormationType formation) =>
        Assert.DoesNotContain(PlayerPosition.GK, formation.DefaultPositions());

    [Theory]
    [MemberData(nameof(AllFormations))]
    public void Every_formation_has_a_display_name_that_is_not_the_enum_name(FormationType formation)
    {
        var name = formation.DisplayName();

        Assert.NotEqual(formation.ToString(), name);
        Assert.Contains('-', name);
    }

    [Theory]
    [MemberData(nameof(AllFormations))]
    public void The_display_name_adds_up_to_the_outfield_players_it_fields(FormationType formation)
    {
        // "4-2-3-1" describes ten players; a qualifier like "4-4-2 diamond" is not part of the count.
        var total = formation.DisplayName().Split(' ')[0].Split('-').Sum(int.Parse);

        Assert.Equal(formation.DefaultPositions().Length, total);
    }

    [Fact]
    public void A_formations_defence_matches_the_number_its_name_claims()
    {
        Assert.Equal(4, FormationType.F442.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(3, FormationType.F352.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(5, FormationType.F532.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
    }

    [Fact]
    public void The_diamond_hangs_a_holding_and_an_attacking_midfielder_off_a_flat_four()
    {
        var positions = FormationType.F442Diamond.DefaultPositions();

        Assert.Equal(4, positions.Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(1, positions.Count(p => p == PlayerPosition.CDM));
        Assert.Equal(1, positions.Count(p => p == PlayerPosition.CAM));
        Assert.Equal(2, positions.Count(p => p == PlayerPosition.ST));
    }

    [Fact]
    public void Every_formation_is_offered_once_under_its_own_format_in_the_order_its_name_reads()
    {
        var offered = MatchFormatExtensions.Descending.SelectMany(FormationTypeExtensions.Alphabetical).ToList();

        Assert.Equal(Enum.GetValues<FormationType>().Length, offered.Distinct().Count());

        foreach (var format in MatchFormatExtensions.Descending)
        {
            var shapes = FormationTypeExtensions.Alphabetical(format);
            var names = shapes.Select(f => f.DisplayName()).ToList();

            Assert.All(shapes, f => Assert.Equal(format, f.Format()));
            Assert.Equal([.. names.Order(StringComparer.Ordinal)], names);
        }
    }

    [Fact]
    public void Nine_a_side_fields_eight_outfield_players_and_opens_on_three_three_two()
    {
        var nine = FormationTypeExtensions.Alphabetical(MatchFormat.NineASide);

        Assert.Contains(FormationType.F332, nine);
        Assert.All(nine, f => Assert.Equal(8, f.DefaultPositions().Length));
        Assert.Equal(FormationType.F332, MatchFormat.NineASide.DefaultFormation());
    }

    [Fact]
    public void Three_three_two_is_a_back_three_a_midfield_three_and_two_up_front()
    {
        var positions = FormationType.F332.DefaultPositions();

        Assert.Equal(3, positions.Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(3, positions.Count(p => p.Category() == PositionCategory.Midfielder));
        Assert.Equal(2, positions.Count(p => p == PlayerPosition.ST));
    }

    [Fact]
    public void Every_match_format_offers_a_shape_and_a_default_that_fields_its_own_number()
    {
        foreach (var format in MatchFormatExtensions.Descending)
        {
            Assert.NotEmpty(FormationTypeExtensions.Alphabetical(format));
            Assert.Equal(format, format.DefaultFormation().Format());
        }
    }

    [Fact]
    public void Every_match_format_is_named_for_the_number_it_fields()
    {
        // The display name is also the resx key, so a drift on either side renders the enum member in the picker with no warning.
        foreach (var format in MatchFormatExtensions.Descending)
        {
            Assert.Equal($"{format.PlayerCount()} vs {format.PlayerCount()}", format.DisplayName());
        }
    }

    [Fact]
    public void The_formats_are_offered_largest_first()
    {
        Assert.Equal([MatchFormat.ElevenASide, MatchFormat.NineASide], MatchFormatExtensions.Descending);
    }

    [Theory]
    [InlineData(PlayerPosition.GK, PositionCategory.Goalkeeper)]
    [InlineData(PlayerPosition.CB, PositionCategory.Defender)]
    [InlineData(PlayerPosition.CM, PositionCategory.Midfielder)]
    [InlineData(PlayerPosition.ST, PositionCategory.Forward)]
    public void Positions_fall_into_the_expected_category(PlayerPosition position, PositionCategory expected) =>
        Assert.Equal(expected, position.Category());
}
