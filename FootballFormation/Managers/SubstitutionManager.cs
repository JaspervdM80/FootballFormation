using FootballFormation.Models;

namespace FootballFormation.Managers;

public interface ISubstitutionManager
{
    List<Substitution> GenerateSubstitutions(List<Formation> formations, Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> keepers);
}

public class SubstitutionManager : ISubstitutionManager
{
    private const int MAX_SUBSTITUTIONS_PER_MOMENT = 4;

    public List<Substitution> GenerateSubstitutions(List<Formation> formations, Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> keepers)
    {
        var substitutions = new List<Substitution>();

        // Add goalkeeper substitution at half-time (minute 30) if keepers change
        AddGoalkeeperSubstitution(substitutions, formations, keepers);

        // Add field player substitutions at 15, 30, and 45 minutes
        var substitutionMoments = new[] { 15, 30, 45 };

        foreach (var minute in substitutionMoments)
        {
            var formationIndex = minute / 15; // 1, 2, 3
            if (formationIndex < formations.Count)
            {
                AddFieldPlayerSubstitutions(substitutions, formations[formationIndex], playingTimes, minute);
            }
        }

        return substitutions;
    }

    private void AddGoalkeeperSubstitution(List<Substitution> substitutions, List<Formation> formations, List<Player> keepers)
    {
        if (keepers.Count <= 1 || formations.Count < 3)
            return;

        var firstHalfKeeper = formations[1].Goalkeeper; // End of first half
        var secondHalfKeeper = formations[2].Goalkeeper; // Start of second half

        if (firstHalfKeeper != secondHalfKeeper)
        {
            substitutions.Add(new Substitution
            {
                Minute = 30, // Half-time
                PlayerOut = firstHalfKeeper,
                PlayerIn = secondHalfKeeper,
                FromPosition = "GK",
                ToPosition = "GK"
            });
        }
    }

    private void AddFieldPlayerSubstitutions(List<Substitution> substitutions, Formation currentFormation, Dictionary<Player, PlayerPlayingTime> playingTimes, int minute)
    {
        var currentGoalkeeper = currentFormation.Goalkeeper;

        // Find players who need more playing time and are available (not goalkeeper, not currently playing)
        var playersNeedingTime = playingTimes.Values
            .Where(pt => pt.Player != currentGoalkeeper &&
                        pt.NeedsMoreTime &&
                        !currentFormation.PositionedPlayers.Values.Contains(pt.Player))
            .OrderByDescending(pt => pt.MinutesDeficit)
            .Take(MAX_SUBSTITUTIONS_PER_MOMENT)
            .ToList();

        // Find players who can be substituted out (currently playing, have enough time or overplayed)
        var playersToSubOut = playingTimes.Values
            .Where(pt => pt.Player != currentGoalkeeper &&
                        currentFormation.PositionedPlayers.Values.Contains(pt.Player) &&
                        (pt.ActualMinutes >= pt.TargetMinutes || !pt.NeedsMoreTime))
            .OrderByDescending(pt => pt.ActualMinutes - pt.TargetMinutes)
            .Take(Math.Min(MAX_SUBSTITUTIONS_PER_MOMENT, playersNeedingTime.Count))
            .ToList();

        // Create substitutions
        for (int i = 0; i < Math.Min(playersNeedingTime.Count, playersToSubOut.Count); i++)
        {
            var playerIn = playersNeedingTime[i].Player;
            var playerOut = playersToSubOut[i].Player;

            // Find the position of the player being substituted
            var position = currentFormation.PositionedPlayers
                .First(kvp => kvp.Value == playerOut).Key;

            // Make the substitution in the formation
            currentFormation.PositionedPlayers[position] = playerIn;

            // Add substitution record
            substitutions.Add(new Substitution
            {
                Minute = minute,
                PlayerOut = playerOut,
                PlayerIn = playerIn,
                FromPosition = position,
                ToPosition = position
            });

            // Update playing time projections
            var remainingMinutes = 60 - minute;
            playingTimes[playerOut].ActualMinutes -= remainingMinutes;
            playingTimes[playerIn].ActualMinutes += remainingMinutes;
        }
    }
}