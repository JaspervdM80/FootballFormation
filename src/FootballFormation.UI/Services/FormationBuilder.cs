using FootballFormation.UI.Enums;
using FootballFormation.UI.Managers;
using FootballFormation.UI.Models;

namespace FootballFormation.UI.Services;

public interface IFormationBuilder
{
    List<Formation> CreateFormationsForHalf(Squad halfSquad, Dictionary<Player, PlayerPlayingTime> playingTimes, int setupVariation);
}

public class FormationBuilder : IFormationBuilder
{
    private const int REQUIRED_FIELD_PLAYERS = 10;
    private const int PERIOD_DURATION = 15;

    private readonly IPlayingTimeManager _playingTimeManager;

    public FormationBuilder(IPlayingTimeManager playingTimeManager)
    {
        _playingTimeManager = playingTimeManager;
    }

    public List<Formation> CreateFormationsForHalf(Squad squad, Dictionary<Player, PlayerPlayingTime> playingTimes, int setupVariation)
    {
        var formations = new List<Formation>();
        var periodsInHalf = 2; // Each half has 2 periods of 15 minutes

        for (int period = 0; period < periodsInHalf; period++)
        {
            var startMinute = squad.StartMinute + period * PERIOD_DURATION;
            var endMinute = startMinute + PERIOD_DURATION;
            var periodName = GetPeriodName(squad.HalfNumber, period + 1);

            var formation = CreateSingleFormation(
                squad,
                playingTimes,
                periodName,
                startMinute,
                endMinute,
                setupVariation);

            formations.Add(formation);

            // Update playing time after creating formation
            var playersOnField = formation.PositionedPlayers.Values.Where(p => p != null).ToList();
            _playingTimeManager.UpdatePlayingTime(playingTimes, playersOnField, PERIOD_DURATION);
        }

        return formations;
    }

    private Formation CreateSingleFormation(
        Squad halfSquad,
        Dictionary<Player, PlayerPlayingTime> playingTimes,
        string periodName,
        int startMinute,
        int endMinute,
        int setupVariation)
    {
        var formation = new Formation
        {
            Period = periodName,
            StartMinute = startMinute,
            EndMinute = endMinute,
            Goalkeeper = halfSquad.Goalkeeper
        };

        // Select field players based on playing time needs
        var selectedFieldPlayers = _playingTimeManager.SelectPlayersForFormation(
            playingTimes,
            halfSquad.FieldPlayers,
            REQUIRED_FIELD_PLAYERS);

        // Assign positions
        AssignPositions(formation, selectedFieldPlayers, setupVariation);

        // Add remaining players to bench
        formation.Bench = halfSquad.FieldPlayers
            .Where(p => !selectedFieldPlayers.Contains(p))
            .OrderBy(p => playingTimes.ContainsKey(p) ? playingTimes[p].ActualMinutes : int.MaxValue)
            .ToList();

        return formation;
    }

    private void AssignPositions(Formation formation, List<Player> selectedPlayers, int setupVariation)
    {
        var positions = new[]
        {
            ("DC1", Position.DC), ("DC2", Position.DC),
            ("DL", Position.DL), ("DR", Position.DR),
            ("CDM1", Position.CDM), ("CDM2", Position.CDM),
            ("CAM", Position.CAM),
            ("LW", Position.LW), ("ST", Position.ST), ("RW", Position.RW)
        };

        var availablePlayers = new List<Player>(selectedPlayers);

        // Use different assignment strategies based on setup variation
        var assignmentStrategy = setupVariation % 3;

        switch (assignmentStrategy)
        {
            case 0: // Skill-based assignment
                AssignBySkill(formation, availablePlayers, positions);
                break;
            case 1: // Preference-based assignment
                AssignByPreference(formation, availablePlayers, positions);
                break;
            case 2: // Balanced assignment
                AssignByBalance(formation, availablePlayers, positions);
                break;
        }
    }

    private void AssignBySkill(Formation formation, List<Player> availablePlayers, (string Key, Position Pos)[] positions)
    {
        foreach (var (key, position) in positions.OrderByDescending(p => GetPositionImportance(p.Pos)))
        {
            var bestPlayer = availablePlayers
                .OrderByDescending(p => p.GetPositionScore(position))
                .First();

            formation.PositionedPlayers[key] = bestPlayer;
            availablePlayers.Remove(bestPlayer);
        }
    }

    private void AssignByPreference(Formation formation, List<Player> availablePlayers, (string Key, Position Pos)[] positions)
    {
        var assignedPlayers = new HashSet<Player>();

        // First pass: assign players to preferred positions
        foreach (var (key, position) in positions.OrderByDescending(p => GetPositionImportance(p.Pos)))
        {
            var preferredPlayer = availablePlayers
                .Where(p => !assignedPlayers.Contains(p) && p.SecondaryPositions.Contains(position))
                .OrderByDescending(p => p.GetPositionScore(position))
                .FirstOrDefault();

            if (preferredPlayer != null)
            {
                formation.PositionedPlayers[key] = preferredPlayer;
                assignedPlayers.Add(preferredPlayer);
            }
        }

        // Second pass: assign remaining positions by skill
        var remainingPositions = positions.Where(p => !formation.PositionedPlayers.ContainsKey(p.Key)).ToList();
        var remainingPlayers = availablePlayers.Where(p => !assignedPlayers.Contains(p)).ToList();

        foreach (var (key, position) in remainingPositions)
        {
            var bestPlayer = remainingPlayers
                .OrderByDescending(p => p.GetPositionScore(position))
                .First();

            formation.PositionedPlayers[key] = bestPlayer;
            remainingPlayers.Remove(bestPlayer);
        }
    }

    private void AssignByBalance(Formation formation, List<Player> availablePlayers, (string Key, Position Pos)[] positions)
    {
        // Randomize order slightly to create variation
        var random = new Random(availablePlayers.Count);
        var shuffledPositions = positions.OrderBy(p => random.Next()).ToArray();

        foreach (var (key, position) in shuffledPositions)
        {
            var bestPlayer = availablePlayers
                .OrderByDescending(p => p.GetPositionScore(position))
                .First();

            formation.PositionedPlayers[key] = bestPlayer;
            availablePlayers.Remove(bestPlayer);
        }
    }

    private int GetPositionImportance(Position position)
    {
        return position switch
        {
            Position.DC => 10,
            Position.ST => 9,
            Position.CDM => 8,
            Position.CAM => 7,
            Position.DL or Position.DR => 6,
            Position.LW or Position.RW => 5,
            _ => 1
        };
    }

    private string GetPeriodName(int halfNumber, int periodInHalf)
    {
        return halfNumber switch
        {
            1 when periodInHalf == 1 => "Eerste Helft - Start",
            1 when periodInHalf == 2 => "Eerste Helft - Na wissels",
            2 when periodInHalf == 1 => "Tweede Helft - Start",
            2 when periodInHalf == 2 => "Tweede Helft - Na wissels",
            _ => $"Helft {halfNumber} - Periode {periodInHalf}"
        };
    }
}