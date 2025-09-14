namespace FootballFormation.UI.Services;

public interface ISquadCreationService
{
    /// <summary>
    /// Creates a single squad based on player availability and configuration
    /// </summary>
    EnhancedSquad CreateSquad(List<PlayerAvailability> availablePlayers, SquadCreationConfig config);
    
    /// <summary>
    /// Creates multiple squad variations for comparison
    /// </summary>
    List<EnhancedSquad> CreateMultipleSquads(List<PlayerAvailability> availablePlayers, SquadCreationConfig config, int numberOfSquads = 3);
    
    /// <summary>
    /// Validates that squad creation is possible with the given players
    /// </summary>
    SquadCreationValidationResult ValidateSquadCreation(List<PlayerAvailability> availablePlayers, SquadCreationConfig config);
}

public class SquadCreationService : ISquadCreationService
{
    // Standard 4-4-2 formation positions
    private readonly (string Key, Position Position)[] _formationPositions = new[]
    {
        ("GK", Position.GK),
        ("DC1", Position.DC),
        ("DC2", Position.DC),
        ("DL", Position.DL),
        ("DR", Position.DR),
        ("CDM1", Position.CDM),
        ("CDM2", Position.CDM),
        ("CAM", Position.CAM),
        ("LW", Position.LW),
        ("ST", Position.ST),
        ("RW", Position.RW)
    };

    public EnhancedSquad CreateSquad(List<PlayerAvailability> availablePlayers, SquadCreationConfig config)
    {
        var validation = ValidateSquadCreation(availablePlayers, config);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Cannot create squad: {string.Join(", ", validation.ErrorMessages)}");
        }

        var availableForSelection = availablePlayers.Where(pa => pa.IsAvailable).ToList();
        
        var squad = new EnhancedSquad
        {
            SquadId = Random.Shared.Next(1000, 9999),
            Name = $"Squad {DateTime.Now:HHmm}",
            Config = config
        };

        // Step 1: Assign goalkeeper
        var goalkeeper = AssignGoalkeeper(availableForSelection);
        squad.PlayerAssignments.Add(new PlayerAssignment
        {
            Player = goalkeeper.Player,
            AssignedPosition = Position.GK,
            PositionKey = "GK",
            IsStarting = true,
            PlannedMinutes = config.GameDurationMinutes
        });
        
        availableForSelection.Remove(goalkeeper);

        // Step 2: Assign field players based on strategy
        var fieldPlayerAssignments = AssignFieldPlayers(availableForSelection, config);
        squad.PlayerAssignments.AddRange(fieldPlayerAssignments);

        // Step 3: Set bench players
        var assignedPlayers = squad.PlayerAssignments.Select(pa => pa.Player).ToHashSet();
        squad.BenchPlayers = availablePlayers
            .Where(pa => pa.IsAvailable && !assignedPlayers.Contains(pa.Player))
            .OrderBy(pa => pa.Priority)
            .ThenByDescending(pa => pa.Player.Skills.AverageSkill)
            .ToList();

        return squad;
    }

    public List<EnhancedSquad> CreateMultipleSquads(List<PlayerAvailability> availablePlayers, SquadCreationConfig config, int numberOfSquads = 3)
    {
        var squads = new List<EnhancedSquad>();
        
        // Create squads with different strategies
        var strategies = new[]
        {
            SquadCreationStrategy.PreferredPositions,
            SquadCreationStrategy.OptimalPerformance,
            SquadCreationStrategy.BalancedRotation
        };

        for (int i = 0; i < numberOfSquads; i++)
        {
            var strategyConfig = new SquadCreationConfig
            {
                GameDurationMinutes = config.GameDurationMinutes,
                PlayersOnField = config.PlayersOnField,
                MinimumPlayingTimeMinutes = config.MinimumPlayingTimeMinutes,
                AllowPositionFlexibility = config.AllowPositionFlexibility,
                Strategy = strategies[i % strategies.Length]
            };

            var squad = CreateSquad(availablePlayers, strategyConfig);
            squad.SquadId = i + 1;
            squad.Name = $"Squad {i + 1} ({strategyConfig.Strategy})";
            squads.Add(squad);
        }

        return squads.OrderByDescending(s => s.OverallStrength).ToList();
    }

    public SquadCreationValidationResult ValidateSquadCreation(List<PlayerAvailability> availablePlayers, SquadCreationConfig config)
    {
        var result = new SquadCreationValidationResult();
        var availableForSelection = availablePlayers.Where(pa => pa.IsAvailable).ToList();

        // Check minimum players
        if (availableForSelection.Count < config.PlayersOnField)
        {
            result.ErrorMessages.Add($"Need at least {config.PlayersOnField} available players, but only {availableForSelection.Count} are available");
        }

        // Check goalkeeper availability
        var availableKeepers = availableForSelection.Where(pa => pa.CanPlayKeeper).ToList();
        if (!availableKeepers.Any())
        {
            result.ErrorMessages.Add("At least one goalkeeper is required");
        }

        return result;
    }

    private PlayerAvailability AssignGoalkeeper(List<PlayerAvailability> availablePlayers)
    {
        // First try dedicated keepers, then players who can play keeper
        var goalkeeper = availablePlayers
            .Where(pa => pa.CanPlayKeeper)
            .OrderBy(pa => pa.Priority)
            .ThenByDescending(pa => pa.Player.GetPositionScore(Position.GK))
            .First();

        return goalkeeper;
    }

    private List<PlayerAssignment> AssignFieldPlayers(List<PlayerAvailability> availablePlayers, SquadCreationConfig config)
    {
        var fieldPositions = _formationPositions.Where(fp => fp.Position != Position.GK).ToArray();
        var assignments = new List<PlayerAssignment>();
        var availableForAssignment = new List<PlayerAvailability>(availablePlayers);

        switch (config.Strategy)
        {
            case SquadCreationStrategy.PreferredPositions:
                assignments = AssignByPreferredPositions(availableForAssignment, fieldPositions, config);
                break;
            case SquadCreationStrategy.OptimalPerformance:
                assignments = AssignByOptimalPerformance(availableForAssignment, fieldPositions, config);
                break;
            case SquadCreationStrategy.BalancedRotation:
                assignments = AssignByBalancedRotation(availableForAssignment, fieldPositions, config);
                break;
            case SquadCreationStrategy.HybridApproach:
                assignments = AssignByHybridApproach(availableForAssignment, fieldPositions, config);
                break;
        }

        return assignments;
    }

    private List<PlayerAssignment> AssignByPreferredPositions(List<PlayerAvailability> availablePlayers, (string Key, Position Position)[] positions, SquadCreationConfig config)
    {
        var assignments = new List<PlayerAssignment>();
        var assigned = new HashSet<Player>();

        // First pass: assign players to their main positions
        foreach (var (key, position) in positions.OrderByDescending(p => GetPositionImportance(p.Position)))
        {
            var bestPlayer = availablePlayers
                .Where(pa => !assigned.Contains(pa.Player) && pa.Player.MainPosition == position)
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .FirstOrDefault();

            if (bestPlayer != null)
            {
                assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
                assigned.Add(bestPlayer.Player);
            }
        }

        // Second pass: assign players to their secondary positions
        var unassignedPositions = positions.Where(p => !assignments.Any(a => a.PositionKey == p.Key)).ToList();
        foreach (var (key, position) in unassignedPositions)
        {
            var bestPlayer = availablePlayers
                .Where(pa => !assigned.Contains(pa.Player) && pa.Player.SecondaryPositions.Contains(position))
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .FirstOrDefault();

            if (bestPlayer != null)
            {
                assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
                assigned.Add(bestPlayer.Player);
            }
        }

        // Third pass: assign remaining positions by best fit
        unassignedPositions = positions.Where(p => !assignments.Any(a => a.PositionKey == p.Key)).ToList();
        foreach (var (key, position) in unassignedPositions)
        {
            var bestPlayer = availablePlayers
                .Where(pa => !assigned.Contains(pa.Player))
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .FirstOrDefault();

            if (bestPlayer != null)
            {
                assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
                assigned.Add(bestPlayer.Player);
            }
        }

        return assignments;
    }

    private List<PlayerAssignment> AssignByOptimalPerformance(List<PlayerAvailability> availablePlayers, (string Key, Position Position)[] positions, SquadCreationConfig config)
    {
        var assignments = new List<PlayerAssignment>();
        var availableForAssignment = new List<PlayerAvailability>(availablePlayers);

        // Sort positions by importance and assign best available player for each
        foreach (var (key, position) in positions.OrderByDescending(p => GetPositionImportance(p.Position)))
        {
            var bestPlayer = availableForAssignment
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .First();

            assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
            availableForAssignment.Remove(bestPlayer);
        }

        return assignments;
    }

    private List<PlayerAssignment> AssignByBalancedRotation(List<PlayerAvailability> availablePlayers, (string Key, Position Position)[] positions, SquadCreationConfig config)
    {
        var assignments = new List<PlayerAssignment>();
        
        // Sort players by priority and target minutes to ensure fair rotation
        var sortedPlayers = availablePlayers
            .OrderBy(pa => pa.Priority)
            .ThenByDescending(pa => pa.TargetMinutes)
            .ToList();

        var positionQueue = new Queue<(string Key, Position Position)>(positions);

        foreach (var player in sortedPlayers.Take(positions.Length))
        {
            var (key, position) = positionQueue.Dequeue();
            assignments.Add(CreateAssignment(player.Player, position, key, config));
        }

        return assignments;
    }

    private List<PlayerAssignment> AssignByHybridApproach(List<PlayerAvailability> availablePlayers, (string Key, Position Position)[] positions, SquadCreationConfig config)
    {
        // Combine preferred positions with performance optimization
        var assignments = new List<PlayerAssignment>();
        var assigned = new HashSet<Player>();

        // First: assign key positions (GK, DC, ST) by preference
        var keyPositions = positions.Where(p => p.Position == Position.DC || p.Position == Position.ST).ToArray();
        foreach (var (key, position) in keyPositions)
        {
            var bestPlayer = availablePlayers
                .Where(pa => !assigned.Contains(pa.Player) && 
                           (pa.Player.MainPosition == position || pa.Player.SecondaryPositions.Contains(position)))
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .FirstOrDefault();

            if (bestPlayer != null)
            {
                assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
                assigned.Add(bestPlayer.Player);
            }
        }

        // Second: assign remaining positions by optimal performance
        var remainingPositions = positions.Where(p => !assignments.Any(a => a.PositionKey == p.Key)).ToList();
        var remainingPlayers = availablePlayers.Where(pa => !assigned.Contains(pa.Player)).ToList();

        foreach (var (key, position) in remainingPositions)
        {
            var bestPlayer = remainingPlayers
                .OrderBy(pa => pa.Priority)
                .ThenByDescending(pa => pa.Player.GetPositionScore(position))
                .First();

            assignments.Add(CreateAssignment(bestPlayer.Player, position, key, config));
            remainingPlayers.Remove(bestPlayer);
        }

        return assignments;
    }

    private PlayerAssignment CreateAssignment(Player player, Position position, string positionKey, SquadCreationConfig config)
    {
        return new PlayerAssignment
        {
            Player = player,
            AssignedPosition = position,
            PositionKey = positionKey,
            IsStarting = true,
            PlannedMinutes = config.GameDurationMinutes / 2 // Default to half game for rotation
        };
    }

    private int GetPositionImportance(Position position)
    {
        return position switch
        {
            Position.GK => 10,
            Position.DC => 9,
            Position.ST => 8,
            Position.CDM => 7,
            Position.CAM => 6,
            Position.DL or Position.DR => 5,
            Position.LW or Position.RW => 4,
            _ => 1
        };
    }
}

public class SquadCreationValidationResult
{
    public bool IsValid => !ErrorMessages.Any();
    public List<string> ErrorMessages { get; set; } = [];
}