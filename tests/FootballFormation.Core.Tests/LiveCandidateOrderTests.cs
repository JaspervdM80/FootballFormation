namespace FootballFormation.Core.Tests;

public class LiveCandidateOrderTests
{
    private static List<string> Names(IEnumerable<Player> players) => [.. players.Select(p => p.FirstName)];

    [Fact]
    public void Scorers_are_offered_on_the_pitch_first_then_by_most_goals_then_by_name()
    {
        var anna = TestData.Player(1, "Anna");
        var bo = TestData.Player(2, "Bo");
        var cato = TestData.Player(3, "Cato");
        var dewi = TestData.Player(4, "Dewi");
        var seasonGoals = new Dictionary<int, int> { [2] = 3, [4] = 9 };

        var ordered = LiveCandidateOrder.Scorers([anna, bo, cato, dewi], new HashSet<int> { 1, 2, 3 }, seasonGoals, TestData.Game());

        // Dewi has the most goals but is not on the pitch, which outranks any tally.
        Assert.Equal(["Bo", "Anna", "Cato", "Dewi"], Names(ordered));
    }

    [Fact]
    public void A_goal_already_scored_in_this_match_counts_towards_the_order_but_an_own_goal_does_not()
    {
        var anna = TestData.Player(1, "Anna");
        var bo = TestData.Player(2, "Bo");
        var game = TestData.Game();
        game.Goals.Add(new GameGoal { ScorerId = 2 });
        game.Goals.Add(new GameGoal { ScorerId = 1, IsOwnGoal = true });
        game.Goals.Add(new GameGoal { ScorerId = 1, IsOwnGoal = true });

        var ordered = LiveCandidateOrder.Scorers([anna, bo], new HashSet<int> { 1, 2 }, new Dictionary<int, int>(), game);

        Assert.Equal(["Bo", "Anna"], Names(ordered));
    }

    [Fact]
    public void The_bench_is_offered_best_fit_for_the_vacated_position_first_then_by_name()
    {
        var bench = new[]
        {
            TestData.Player(1, "Anna", PlayerPosition.ST),
            TestData.Player(2, "Bo", PlayerPosition.ST, null, PlayerPosition.CB),
            TestData.Player(3, "Cato", PlayerPosition.DEF),
            TestData.Player(4, "Dewi", PlayerPosition.CB),
            TestData.Player(5, "Eva", PlayerPosition.CB),
        };

        var ordered = LiveCandidateOrder.ForPosition(bench, PlayerPosition.CB);

        Assert.Equal(["Dewi", "Eva", "Cato", "Bo", "Anna"], Names(ordered));
    }
}
