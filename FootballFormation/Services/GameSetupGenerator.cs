using FootballFormation.Enums;
using FootballFormation.Managers;
using FootballFormation.Models;

namespace FootballFormation.Services;

public class GameSetupGenerator
{
    private const int TARGET_SETUPS = 5;

    private readonly ISquadManager _squadManager;
    private readonly IPlayingTimeManager _playingTimeManager;
    private readonly ISubstitutionManager _substitutionManager;
    private readonly IFormationBuilder _formationBuilder;

    public GameSetupGenerator(
        ISquadManager squadManager,
        IPlayingTimeManager playingTimeManager,
        ISubstitutionManager substitutionManager,
        IFormationBuilder formationBuilder)
    {
        _squadManager = squadManager;
        _playingTimeManager = playingTimeManager;
        _substitutionManager = substitutionManager;
        _formationBuilder = formationBuilder;
    }

    public List<GameSetup> GenerateGameSetups(List<Player> allPlayers)
    {
        var availablePlayers = allPlayers.Where(p => !p.IsAbsent).ToList();
        var keepers = availablePlayers.Where(p => p.IsKeeper).ToList();

        if (!keepers.Any())
            throw new InvalidOperationException("At least one goalkeeper is required");

        if (availablePlayers.Count < 11)
            throw new InvalidOperationException("At least 11 players (1 goalkeeper + 10 field players) are required");

        var gameSetups = new List<GameSetup>();

        // Reset playing time for all calculations
        foreach (var player in availablePlayers)
        {
            player.MinutesPlayed = 0;
        }

        for (int setupId = 1; setupId <= TARGET_SETUPS; setupId++)
        {
            var gameSetup = GenerateSingleGameSetup(availablePlayers, keepers, setupId);
            gameSetups.Add(gameSetup);
        }

        // Sort primarily by playing time fairness, then by team strength
        return gameSetups
            .OrderBy(gs => gs.PlayingTimeFairness)
            .ThenByDescending(gs => gs.TotalTeamStrength)
            .ToList();
    }

    private GameSetup GenerateSingleGameSetup(List<Player> availablePlayers, List<Player> keepers, int setupId)
    {
        var gameSetup = new GameSetup { SetupId = setupId };

        // Reset minutes for this setup calculation
        foreach (var player in availablePlayers)
        {
            player.MinutesPlayed = 0;
        }

        // Create half squads using SquadManager
        var (firstHalfSquad, secondHalfSquad) = _squadManager.CreateHalfSquads(availablePlayers, setupId);

        // Calculate playing time plan using PlayingTimeManager
        var fieldPlayers = availablePlayers.Where(p => !p.IsKeeper).ToList();
        var playingTimes = _playingTimeManager.CalculatePlayingTimePlan(fieldPlayers);

        // Create formations for each half using FormationBuilder
        var firstHalfFormations = _formationBuilder.CreateFormationsForHalf(firstHalfSquad, playingTimes, setupId);
        var secondHalfFormations = _formationBuilder.CreateFormationsForHalf(secondHalfSquad, playingTimes, setupId);

        var allFormations = new List<Formation>();
        allFormations.AddRange(firstHalfFormations);
        allFormations.AddRange(secondHalfFormations);

        gameSetup.Formations = allFormations;

        // Generate substitutions using SubstitutionManager
        gameSetup.Substitutions = _substitutionManager.GenerateSubstitutions(allFormations, playingTimes, keepers);

        // Apply substitutions to get final playing time
        ApplySubstitutionsToPlayingTime(gameSetup, playingTimes);

        // Calculate final metrics
        CalculateSetupMetrics(gameSetup, availablePlayers, playingTimes);

        return gameSetup;
    }

    private void ApplySubstitutionsToPlayingTime(GameSetup gameSetup, Dictionary<Player, PlayerPlayingTime> playingTimes)
    {
        foreach (var substitution in gameSetup.Substitutions)
        {
            var formation = gameSetup.Formations.First(f => f.StartMinute <= substitution.Minute && f.EndMinute > substitution.Minute);
            var minutesLeft = formation.EndMinute - substitution.Minute;

            if (playingTimes.ContainsKey(substitution.PlayerOut))
            {
                playingTimes[substitution.PlayerOut].ActualMinutes -= minutesLeft;
            }

            if (playingTimes.ContainsKey(substitution.PlayerIn))
            {
                playingTimes[substitution.PlayerIn].ActualMinutes += minutesLeft;
            }
        }
    }

    private void CalculateSetupMetrics(GameSetup gameSetup, List<Player> availablePlayers, Dictionary<Player, PlayerPlayingTime> playingTimes)
    {
        // Update player objects with final playing time
        foreach (var player in availablePlayers)
        {
            if (playingTimes.ContainsKey(player))
            {
                player.MinutesPlayed = playingTimes[player].ActualMinutes;
            }
            else
            {
                // For goalkeepers, calculate their playing time from formations
                var keeperMinutes = gameSetup.Formations.Sum(f =>
                    f.Goalkeeper == player ? f.EndMinute - f.StartMinute : 0);
                player.MinutesPlayed = keeperMinutes;
            }
        }

        // Calculate total team strength
        gameSetup.TotalTeamStrength = gameSetup.Formations.Average(f => f.TeamStrength);

        // Calculate playing time fairness using PlayingTimeManager
        gameSetup.PlayingTimeFairness = _playingTimeManager.IsPlayingTimeFair(playingTimes) ? 0 : 1;

        // Calculate more detailed fairness metrics
        var allPlayerMinutes = availablePlayers
            .Select(p => p.MinutesPlayed)
            .Where(minutes => minutes > 0)
            .ToList();

        if (allPlayerMinutes.Any())
        {
            gameSetup.PlayingTimeVariance = allPlayerMinutes.Max() - allPlayerMinutes.Min();

            var average = allPlayerMinutes.Average();
            var sumOfSquares = allPlayerMinutes.Sum(x => (x - average) * (x - average));
            gameSetup.PlayingTimeFairness = Math.Sqrt(sumOfSquares / allPlayerMinutes.Count);
        }
    }
}