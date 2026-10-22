namespace FootballFormation.Core.Tests;

public class PastedSquadListTests
{
    [Fact]
    public void A_leading_number_is_the_shirt_the_first_word_the_first_name_and_the_rest_the_surname()
    {
        var lines = PastedSquadList.Parse("7 Sanne de Vries\nLotte\n12 Femke Jansen");

        Assert.Equal(3, lines.Count);
        Assert.Equal((7, "Sanne", "de Vries"), (lines[0].ShirtNumber!.Value, lines[0].FirstName, lines[0].Surname!));
        Assert.Null(lines[1].ShirtNumber);
        Assert.Equal("Lotte", lines[1].FirstName);
        Assert.Null(lines[1].Surname);
        Assert.Equal(12, lines[2].ShirtNumber);
        Assert.All(lines, l => Assert.True(l.IsValid));
    }

    [Fact]
    public void Blank_lines_are_dropped_but_line_numbers_still_point_at_the_pasted_text()
    {
        var lines = PastedSquadList.Parse("\r\n  Sanne  \r\n\r\n\tLotte   de  Vries\r\n   ");

        Assert.Equal([2, 4], lines.Select(l => l.LineNumber));
        Assert.Equal("Sanne", lines[0].DisplayName);
        Assert.Equal("Lotte de Vries", lines[1].DisplayName);
    }

    [Theory]
    [InlineData("#7 Sanne")]
    [InlineData("7. Sanne")]
    [InlineData("7) Sanne")]
    [InlineData("7\tSanne")]
    public void The_usual_ways_of_writing_a_shirt_number_all_read_as_one(string line)
    {
        var parsed = Assert.Single(PastedSquadList.Parse(line));

        Assert.Equal(7, parsed.ShirtNumber);
        Assert.Equal("Sanne", parsed.FirstName);
    }

    [Fact]
    public void Digits_run_into_a_name_are_part_of_the_name_not_a_shirt_number()
    {
        var parsed = Assert.Single(PastedSquadList.Parse("7Sanne"));

        Assert.Null(parsed.ShirtNumber);
        Assert.Equal("7Sanne", parsed.FirstName);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("#12   ")]
    public void A_number_with_no_name_is_flagged(string line)
    {
        Assert.Equal(PastedLineProblem.MissingName, Assert.Single(PastedSquadList.Parse(line)).Problem);
    }

    [Theory]
    [InlineData("0 Sanne")]
    [InlineData("100 Sanne")]
    [InlineData("99999999999 Sanne")]
    public void A_shirt_number_outside_1_to_99_is_flagged(string line)
    {
        Assert.Equal(PastedLineProblem.ShirtNumberOutOfRange, Assert.Single(PastedSquadList.Parse(line)).Problem);
    }

    [Fact]
    public void Every_new_player_sharing_a_shirt_number_is_flagged_since_nothing_says_which_is_wrong()
    {
        var plan = PastedSquadList.Plan(PastedSquadList.Parse("7 Sanne\n7 Lotte\n8 Femke"), [], []);

        Assert.Equal(
            [PastedLineAction.Invalid, PastedLineAction.Invalid, PastedLineAction.Create],
            plan.Select(e => e.Action));
        Assert.Equal(
            [PastedLineProblem.DuplicateShirtNumber, PastedLineProblem.DuplicateShirtNumber, PastedLineProblem.None],
            plan.Select(e => e.Line.Problem));
    }

    [Fact]
    public void A_number_pasted_beside_a_player_already_on_file_cannot_clash_because_it_is_not_saved()
    {
        var earlier = new Player { Id = 2, FirstName = "Lotte", ShirtNumber = 9 };

        var plan = PastedSquadList.Plan(PastedSquadList.Parse("7 Sanne\n7 Lotte"), [], [earlier]);

        Assert.Equal([PastedLineAction.Create, PastedLineAction.AddExisting], plan.Select(e => e.Action));
    }

    [Fact]
    public void A_name_listed_twice_is_flagged_whatever_its_case_or_spacing()
    {
        var lines = PastedSquadList.Parse("Sanne de Vries\nsanne  DE vries\nLotte");

        Assert.Equal(
            [PastedLineProblem.DuplicateName, PastedLineProblem.DuplicateName, PastedLineProblem.None],
            lines.Select(l => l.Problem));
    }

    [Fact]
    public void An_invalid_line_does_not_count_towards_a_duplicate()
    {
        var lines = PastedSquadList.Parse("100 Sanne\n7 Sanne");

        Assert.Equal([PastedLineProblem.ShirtNumberOutOfRange, PastedLineProblem.None], lines.Select(l => l.Problem));
    }

    [Fact]
    public void Nothing_pasted_is_an_empty_list()
    {
        Assert.Empty(PastedSquadList.Parse(null));
        Assert.Empty(PastedSquadList.Parse(" \n \n"));
    }

    [Fact]
    public void The_plan_skips_the_squad_reuses_an_earlier_player_and_creates_the_rest()
    {
        var inSquad = new Player { Id = 1, FirstName = "Sanne", Surname = "de Vries" };
        var earlier = new Player { Id = 2, FirstName = "Lotte" };
        var lines = PastedSquadList.Parse("7 sanne de vries\nLOTTE\n12 Femke Jansen\n0 Bad");

        var plan = PastedSquadList.Plan(lines, [inSquad], [earlier]);

        Assert.Equal(
            [PastedLineAction.AlreadyInSquad, PastedLineAction.AddExisting, PastedLineAction.Create, PastedLineAction.Invalid],
            plan.Select(e => e.Action));
        Assert.Same(inSquad, plan[0].Existing);
        Assert.Same(earlier, plan[1].Existing);
        Assert.Null(plan[2].Existing);
    }

    [Fact]
    public void A_first_name_alone_does_not_match_an_earlier_player_who_has_a_surname()
    {
        var plan = PastedSquadList.Plan(PastedSquadList.Parse("Lotte"), [], [new Player { Id = 2, FirstName = "Lotte", Surname = "Bakker" }]);

        Assert.Equal(PastedLineAction.Create, Assert.Single(plan).Action);
    }

    [Fact]
    public void Two_earlier_players_sharing_a_name_resolve_to_the_first_offered()
    {
        var first = new Player { Id = 5, FirstName = "Lotte" };
        var second = new Player { Id = 3, FirstName = "Lotte" };

        var plan = PastedSquadList.Plan(PastedSquadList.Parse("Lotte"), [], [first, second]);

        Assert.Same(first, Assert.Single(plan).Existing);
    }
}
