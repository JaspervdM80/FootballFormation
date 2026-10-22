namespace FootballFormation.Core.Tests;

public class PastedSquadListTests
{
    private static IReadOnlyList<PastedSquadEntry> Plan(string? text) => PastedSquadList.Plan(text, [], []);

    [Fact]
    public void A_leading_number_is_the_shirt_the_first_word_the_first_name_and_the_rest_the_surname()
    {
        var plan = Plan("7 Sanne de Vries\nLotte\n12 Femke Jansen");

        Assert.Equal(3, plan.Count);
        Assert.Equal((7, "Sanne", "de Vries"), (plan[0].Player.ShirtNumber!.Value, plan[0].Player.FirstName, plan[0].Player.Surname!));
        Assert.Null(plan[1].Player.ShirtNumber);
        Assert.Equal("Lotte", plan[1].Player.FirstName);
        Assert.Null(plan[1].Player.Surname);
        Assert.Equal(12, plan[2].Player.ShirtNumber);
        Assert.All(plan, e => Assert.True(e.IsValid && e.Action == PastedLineAction.Create));
    }

    [Fact]
    public void Blank_lines_are_dropped_but_line_numbers_still_point_at_the_pasted_text()
    {
        var plan = Plan("\r\n  Sanne  \r\n\r\n\tLotte   de  Vries\r\n   ");

        Assert.Equal([2, 4], plan.Select(e => e.LineNumber));
        Assert.Equal("Sanne", plan[0].Player.DisplayName);
        Assert.Equal("Lotte de Vries", plan[1].Player.DisplayName);
    }

    [Theory]
    [InlineData("#7 Sanne")]
    [InlineData("7. Sanne")]
    [InlineData("7) Sanne")]
    [InlineData("7\tSanne")]
    public void The_usual_ways_of_writing_a_shirt_number_all_read_as_one(string line)
    {
        var entry = Assert.Single(Plan(line));

        Assert.Equal(7, entry.Player.ShirtNumber);
        Assert.Equal("Sanne", entry.Player.FirstName);
    }

    [Fact]
    public void Digits_run_into_a_name_are_part_of_the_name_not_a_shirt_number()
    {
        var entry = Assert.Single(Plan("7Sanne"));

        Assert.Null(entry.Player.ShirtNumber);
        Assert.Equal("7Sanne", entry.Player.FirstName);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("#12   ")]
    public void A_number_with_no_name_is_flagged(string line)
    {
        Assert.Equal(PastedLineProblem.MissingName, Assert.Single(Plan(line)).Problem);
    }

    [Theory]
    [InlineData("0 Sanne")]
    [InlineData("100 Sanne")]
    [InlineData("99999999999 Sanne")]
    public void A_shirt_number_outside_1_to_99_is_flagged(string line)
    {
        Assert.Equal(PastedLineProblem.ShirtNumberOutOfRange, Assert.Single(Plan(line)).Problem);
    }

    [Fact]
    public void Every_new_player_sharing_a_shirt_number_is_flagged_since_nothing_says_which_is_wrong()
    {
        Assert.Equal(
            [PastedLineProblem.DuplicateShirtNumber, PastedLineProblem.DuplicateShirtNumber, PastedLineProblem.None],
            Plan("7 Sanne\n7 Lotte\n8 Femke").Select(e => e.Problem));
    }

    [Fact]
    public void A_number_pasted_beside_a_player_already_on_file_cannot_clash_because_it_is_not_saved()
    {
        var earlier = new Player { Id = 2, FirstName = "Lotte", ShirtNumber = 9 };

        var plan = PastedSquadList.Plan("7 Sanne\n7 Lotte", [], [earlier]);

        Assert.All(plan, e => Assert.True(e.IsValid));
        Assert.Equal(9, plan[1].Player.ShirtNumber);
    }

    [Fact]
    public void A_name_listed_twice_is_flagged_whatever_its_case_or_spacing()
    {
        Assert.Equal(
            [PastedLineProblem.DuplicateName, PastedLineProblem.DuplicateName, PastedLineProblem.None],
            Plan("Sanne de Vries\nsanne  DE vries\nLotte").Select(e => e.Problem));
    }

    [Fact]
    public void An_invalid_line_does_not_count_towards_a_duplicate()
    {
        Assert.Equal(
            [PastedLineProblem.ShirtNumberOutOfRange, PastedLineProblem.None],
            Plan("100 Sanne\n7 Sanne").Select(e => e.Problem));
    }

    [Fact]
    public void Nothing_pasted_is_an_empty_plan()
    {
        Assert.Empty(Plan(null));
        Assert.Empty(Plan(" \n \n"));
    }

    [Fact]
    public void The_plan_skips_the_squad_reuses_an_earlier_player_and_creates_the_rest()
    {
        var inSquad = new Player { Id = 1, FirstName = "Sanne", Surname = "de Vries" };
        var earlier = new Player { Id = 2, FirstName = "Lotte" };

        var plan = PastedSquadList.Plan("7 sanne de vries\nLOTTE\n12 Femke Jansen", [inSquad], [earlier]);

        Assert.Equal(
            [PastedLineAction.AlreadyInSquad, PastedLineAction.AddExisting, PastedLineAction.Create],
            plan.Select(e => e.Action));
        Assert.Same(inSquad, plan[0].Player);
        Assert.Same(earlier, plan[1].Player);
        Assert.Equal(0, plan[2].Player.Id);
    }

    [Fact]
    public void A_first_name_alone_does_not_match_an_earlier_player_who_has_a_surname()
    {
        var plan = PastedSquadList.Plan("Lotte", [], [new Player { Id = 2, FirstName = "Lotte", Surname = "Bakker" }]);

        Assert.Equal(PastedLineAction.Create, Assert.Single(plan).Action);
    }

    [Fact]
    public void Two_earlier_players_sharing_a_name_resolve_to_the_first_offered()
    {
        var first = new Player { Id = 5, FirstName = "Lotte" };
        var second = new Player { Id = 3, FirstName = "Lotte" };

        var plan = PastedSquadList.Plan("Lotte", [], [first, second]);

        Assert.Same(first, Assert.Single(plan).Player);
    }
}
