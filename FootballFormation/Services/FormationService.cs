using FootballFormation.Enums;
using FootballFormation.Managers;
using FootballFormation.Models;

namespace FootballFormation.Services;

public class FormationService : IFormationService
{
    private readonly List<Player> _players = [];
    private readonly List<Formation> _formations = [];
    private readonly List<Substitution> _substitutions = [];

    private readonly ISquadManager _squadManager;
    private readonly IPlayingTimeManager _playingTimeManager;
    private readonly ISubstitutionManager _substitutionManager;
    private readonly IFormationBuilder _formationBuilder;

    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Formation> Formations => _formations;
    public IReadOnlyList<Substitution> Substitutions => _substitutions;

    public FormationService(
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

    public void LoadPlayers(List<Player> players)
    {
        Console.WriteLine("LoadPlayers called with " + players.Count + " players");
        _players.Clear();
        _players.AddRange(players);
        GenerateFormations();
    }

    private void GenerateFormations()
    {
        Console.WriteLine("Generating formations...");
        _formations.Clear();
        _substitutions.Clear();

        var availablePlayers = _players.Where(p => !p.IsAbsent).ToList();
        Console.WriteLine($"Available players: {availablePlayers.Count}");

        var keepers = availablePlayers.Where(p => p.IsKeeper).ToList();
        Console.WriteLine($"Keepers: {keepers.Count}");

        if (!keepers.Any())
            throw new InvalidOperationException("At least one goalkeeper is required");

        if (availablePlayers.Count < 11)
            throw new InvalidOperationException("At least 11 players (1 goalkeeper + 10 field players) are required");

        // Reset playing time
        foreach (var player in availablePlayers)
        {
            player.MinutesPlayed = 0;
        }

        // Use SquadManager to create half squads
        var (firstHalfSquad, secondHalfSquad) = _squadManager.CreateHalfSquads(availablePlayers, setupVariation: 1);

        // Use PlayingTimeManager to calculate playing time plan
        var fieldPlayers = availablePlayers.Where(p => !p.IsKeeper).ToList();
        var playingTimes = _playingTimeManager.CalculatePlayingTimePlan(fieldPlayers);

        // Use FormationBuilder to create formations for each half
        var firstHalfFormations = _formationBuilder.CreateFormationsForHalf(firstHalfSquad, playingTimes, setupVariation: 1);
        var secondHalfFormations = _formationBuilder.CreateFormationsForHalf(secondHalfSquad, playingTimes, setupVariation: 1);

        _formations.AddRange(firstHalfFormations);
        _formations.AddRange(secondHalfFormations);

        // Use SubstitutionManager to generate substitutions
        var substitutions = _substitutionManager.GenerateSubstitutions(_formations, playingTimes, keepers);
        _substitutions.AddRange(substitutions);

        // Apply substitutions to get final playing times
        ApplySubstitutionsToPlayingTime(playingTimes);

        // Update player minutes
        UpdatePlayerMinutes(playingTimes, keepers);
    }

    private void ApplySubstitutionsToPlayingTime(Dictionary<Player, PlayerPlayingTime> playingTimes)
    {
        foreach (var substitution in _substitutions)
        {
            var formation = _formations.First(f => f.StartMinute <= substitution.Minute && f.EndMinute > substitution.Minute);
            var minutesLeft = formation.EndMinute - substitution.Minute;

            if (playingTimes.ContainsKey(substitution.PlayerOut))
            {
                playingTimes[substitution.PlayerOut].ActualMinutes -= minutesLeft;
            }

            if (playingTimes.ContainsKey(substitution.PlayerIn))
            {
                playingTimes[substitution.PlayerIn].ActualMinutes += minutesLeft;
            }

            // Update the formation to reflect the substitution
            var position = _formations
                .SelectMany(f => f.PositionedPlayers.Where(kvp => kvp.Value == substitution.PlayerOut))
                .First().Key;

            formation.PositionedPlayers[position] = substitution.PlayerIn;
        }
    }

    private void UpdatePlayerMinutes(Dictionary<Player, PlayerPlayingTime> playingTimes, List<Player> keepers)
    {
        // Update field players with their final playing time
        foreach (var kvp in playingTimes)
        {
            kvp.Key.MinutesPlayed = kvp.Value.ActualMinutes;
        }

        // Update goalkeepers with their playing time
        foreach (var keeper in keepers)
        {
            var keeperMinutes = _formations.Sum(f =>
                f.Goalkeeper == keeper ? f.EndMinute - f.StartMinute : 0);
            
            // If keeper also played as field player, add that time
            if (playingTimes.ContainsKey(keeper))
            {
                keeper.MinutesPlayed = keeperMinutes + playingTimes[keeper].ActualMinutes;
            }
            else
            {
                keeper.MinutesPlayed = keeperMinutes;
            }
        }

        // Log playing time distribution
        var totalMinutes = _players.Sum(p => p.MinutesPlayed);
        var averageMinutes = totalMinutes / _players.Count(p => !p.IsAbsent);
        Console.WriteLine($"Average minutes per player: {averageMinutes}");
        
        foreach (var player in _players.Where(p => !p.IsAbsent).OrderByDescending(p => p.MinutesPlayed))
        {
            Console.WriteLine($"{player.Name}: {player.MinutesPlayed} minutes");
        }
    }
}
