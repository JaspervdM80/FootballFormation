namespace FootballFormation.Core.Tests;

public class FormationTypeTests
{
    public static TheoryData<FormationType> AllFormations() => [.. Enum.GetValues<FormationType>()];

    [Theory]
    [MemberData(nameof(AllFormations))]
    public void Every_formation_fields_ten_outfield_players(FormationType formation)
    {
        // The pitch builds its slots as [GK, ..DefaultPositions()]. Anything but ten here silently
        // produces a pitch with the wrong number of chips.
        Assert.Equal(10, formation.DefaultPositions().Length);
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
    public void The_display_name_adds_up_to_ten_outfield_players(FormationType formation)
    {
        // "4-2-3-1" describes ten players; a typo in the label would misname the shape.
        var total = formation.DisplayName().Split('-').Sum(int.Parse);

        Assert.Equal(10, total);
    }

    [Fact]
    public void A_formations_defence_matches_the_number_its_name_claims()
    {
        Assert.Equal(4, FormationType.F442.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(3, FormationType.F352.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
        Assert.Equal(5, FormationType.F532.DefaultPositions().Count(p => p.Category() == PositionCategory.Defender));
    }

    [Theory]
    [InlineData(PlayerPosition.GK, PositionCategory.Goalkeeper)]
    [InlineData(PlayerPosition.CB, PositionCategory.Defender)]
    [InlineData(PlayerPosition.CM, PositionCategory.Midfielder)]
    [InlineData(PlayerPosition.ST, PositionCategory.Forward)]
    public void Positions_fall_into_the_expected_category(PlayerPosition position, PositionCategory expected) =>
        Assert.Equal(expected, position.Category());
}
