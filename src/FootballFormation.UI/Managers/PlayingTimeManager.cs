using FootballFormation.UI.Models;

namespace FootballFormation.UI.Managers;

public interface IPlayingTimeManager
{
    Dictionary<Player, PlayerPlayingTime> CalculatePlayingTimePlan(List<Player> fieldPlayers);
    void UpdatePlayingTime(Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> playersOnField, int minutes);
    List<Player> SelectPlayersForFormation(Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> availableFieldPlayers, int requiredCount);
    bool IsPlayingTimeFair(Dictionary<Player, PlayerPlayingTime> playingTimes, int maxDeviation = 15);
}

public class PlayingTimeManager : IPlayingTimeManager
{
    private const int TOTAL_GAME_MINUTES = 60;
    private const int REQUIRED_FIELD_PLAYERS = 10;

    public Dictionary<Player, PlayerPlayingTime> CalculatePlayingTimePlan(List<Player> fieldPlayers)
    {
        var totalFieldPlayerMinutes = TOTAL_GAME_MINUTES * REQUIRED_FIELD_PLAYERS;
        var targetMinutesPerPlayer = totalFieldPlayerMinutes / fieldPlayers.Count;

        var playingTimes = new Dictionary<Player, PlayerPlayingTime>();

        foreach (var player in fieldPlayers)
        {
            playingTimes[player] = new PlayerPlayingTime
            {
                Player = player,
                TargetMinutes = (int)Math.Round((decimal)targetMinutesPerPlayer),
                ActualMinutes = 0,
                Priority = 0
            };
        }

        return playingTimes;
    }

    public void UpdatePlayingTime(Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> playersOnField, int minutes)
    {
        foreach (var player in playersOnField)
        {
            if (playingTimes.ContainsKey(player))
            {
                playingTimes[player].ActualMinutes += minutes;
            }
        }

        // Recalculate priorities after updating minutes
        UpdatePriorities(playingTimes);
    }

    public List<Player> SelectPlayersForFormation(Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> availableFieldPlayers, int requiredCount)
    {
        // Update priorities first
        UpdatePriorities(playingTimes);

        return availableFieldPlayers
            .Where(p => playingTimes.ContainsKey(p))
            .OrderByDescending(p => playingTimes[p].Priority)
            .Take(requiredCount)
            .ToList();
    }

    public bool IsPlayingTimeFair(Dictionary<Player, PlayerPlayingTime> playingTimes, int maxDeviation = 15)
    {
        var actualMinutes = playingTimes.Values.Select(pt => pt.ActualMinutes).ToList();
        if (!actualMinutes.Any()) return true;

        var minMinutes = actualMinutes.Min();
        var maxMinutes = actualMinutes.Max();
        return maxMinutes - minMinutes <= maxDeviation;
    }

    private void UpdatePriorities(Dictionary<Player, PlayerPlayingTime> playingTimes)
    {
        foreach (var playingTime in playingTimes.Values)
        {
            // Base priority on minutes deficit (higher deficit = higher priority)
            playingTime.Priority = playingTime.MinutesDeficit * 3.0;

            // Huge boost for players who haven't played at all
            if (playingTime.ActualMinutes == 0)
                playingTime.Priority += 1000;

            // Small skill-based component to break ties (max 5% impact)
            playingTime.Priority += playingTime.Player.Skills.AverageSkill * 0.05;

            // Slight randomness to avoid always picking the same players when tied
            var random = new Random(playingTime.Player.Name.GetHashCode());
            playingTime.Priority += random.NextDouble() * 0.1;
        }
    }
}