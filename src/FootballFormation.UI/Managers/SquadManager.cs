using FootballFormation.UI.Models;

namespace FootballFormation.UI.Managers;
public interface ISquadManager
{
    (Squad FirstHalf, Squad SecondHalf) CreateHalfSquads(List<Player> availablePlayers, int setupVariation);
}

public class SquadManager : ISquadManager
{
    private const int FIRST_HALF_START = 0;
    private const int FIRST_HALF_END = 30;
    private const int SECOND_HALF_START = 30;
    private const int SECOND_HALF_END = 60;

    public (Squad FirstHalf, Squad SecondHalf) CreateHalfSquads(List<Player> availablePlayers, int setupVariation)
    {
        var keepers = availablePlayers.Where(p => p.IsKeeper && !p.IsAbsent).ToList();
        var fieldPlayersPool = availablePlayers.Where(p => !p.IsAbsent).ToList(); // All players can be field players

        if (!keepers.Any())
            throw new InvalidOperationException("At least one goalkeeper is required");

        var (firstHalfKeeper, secondHalfKeeper) = AssignGoalkeepers(keepers, setupVariation);

        var firstHalf = new Squad
        {
            Goalkeeper = firstHalfKeeper,
            FieldPlayers = fieldPlayersPool.Where(p => p != firstHalfKeeper).ToList(),
            HalfNumber = 1,
            StartMinute = FIRST_HALF_START,
            EndMinute = FIRST_HALF_END
        };

        var secondHalf = new Squad
        {
            Goalkeeper = secondHalfKeeper,
            FieldPlayers = fieldPlayersPool.Where(p => p != secondHalfKeeper).ToList(),
            HalfNumber = 2,
            StartMinute = SECOND_HALF_START,
            EndMinute = SECOND_HALF_END
        };

        return (firstHalf, secondHalf);
    }

    private (Player FirstHalf, Player SecondHalf) AssignGoalkeepers(List<Player> keepers, int setupVariation)
    {
        if (keepers.Count == 1)
        {
            // Single keeper plays both halves
            return (keepers[0], keepers[0]);
        }

        // Multiple keepers - swap at half time based on setup variation
        var shuffledKeepers = keepers.OrderBy(k => k.Name.GetHashCode() + setupVariation).ToList();

        return setupVariation % 2 == 0
            ? (shuffledKeepers[0], shuffledKeepers[1])  // First keeper first half, second keeper second half
            : (shuffledKeepers[1], shuffledKeepers[0]); // Second keeper first half, first keeper second half
    }
}