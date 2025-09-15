namespace FootballFormation.UI.Services;

public interface ISquadCreationService
{
    /// <summary>
    /// Creates a single squad based on player availability and configuration
    /// </summary>
    EnhancedSquad CreateSquad(List<PlayerAvailability> availablePlayers, SquadCreationConfig config);
       
    /// <summary>
    /// Validates that squad creation is possible with the given players
    /// </summary>
    SquadCreationValidationResult ValidateSquadCreation(List<PlayerAvailability> availablePlayers, SquadCreationConfig config);

    /// <summary>
    /// Applies half-time substitutions to bring in all substitutes and change keeper if available
    /// </summary>
    void ApplyHalfTimeSubstitutions(EnhancedSquad squad);
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
            PlannedMinutes = config.GameDurationMinutes / 2 // First half only initially
        });
        
        availableForSelection.Remove(goalkeeper);

        // Step 2: Assign field players based on strategy
        var fieldPlayerAssignments = AssignFieldPlayers(availableForSelection, config);
        // Set planned minutes to first half only initially
        foreach (var assignment in fieldPlayerAssignments)
        {
            assignment.PlannedMinutes = config.GameDurationMinutes / 2; // First half only initially
        }
        squad.PlayerAssignments.AddRange(fieldPlayerAssignments);

        // Step 3: Set bench players
        var assignedPlayers = squad.PlayerAssignments.Select(pa => pa.Player).ToHashSet();
        squad.BenchPlayers = availablePlayers
            .Where(pa => pa.IsAvailable && !assignedPlayers.Contains(pa.Player))
            .OrderBy(pa => pa.Priority)
            .ThenByDescending(pa => pa.Player.Skills.AverageSkill)
            .ToList();

        // Step 4: Automatically apply half-time substitutions to show full game view
        ApplyHalfTimeSubstitutions(squad);

        return squad;
    }

    public void ApplyHalfTimeSubstitutions(EnhancedSquad squad)
    {
        Console.WriteLine($"Starting ApplyHalfTimeSubstitutions - Bench players: {squad.BenchPlayers.Count}");
        
        if (squad.BenchPlayers.Count == 0)
        {
            Console.WriteLine("No bench players - extending current player minutes to full game");
            // If no bench players, just extend playing time for current players to full game
            foreach (var assignment in squad.PlayerAssignments)
            {
                assignment.PlannedMinutes = squad.Config.GameDurationMinutes;
                // Update the player's total minutes played
                assignment.Player.MinutesPlayed = assignment.PlannedMinutes;
            }
            return;
        }

        // Get available goalkeepers from bench for potential keeper substitution
        var availableKeepers = squad.BenchPlayers
            .Where(bp => bp.CanPlayKeeper)
            .ToList();

        Console.WriteLine($"Available keepers on bench: {availableKeepers.Count}");

        var currentGoalkeeper = squad.Goalkeeper;
        var halfGameDuration = squad.Config.GameDurationMinutes / 2;

        // Step 1: Handle goalkeeper substitution if multiple keepers available
        if (availableKeepers.Any() && currentGoalkeeper != null)
        {
            Console.WriteLine("Adding goalkeeper substitution");
            var newGoalkeeper = availableKeepers.OrderBy(bp => bp.Priority)
                .ThenByDescending(bp => bp.Player.GetPositionScore(Position.GK))
                .First();

            // Current goalkeeper only plays first half
            currentGoalkeeper.PlannedMinutes = halfGameDuration;
            currentGoalkeeper.Player.MinutesPlayed = halfGameDuration;
            
            // Create new goalkeeper assignment for second half
            var gkAssignment = new PlayerAssignment
            {
                Player = newGoalkeeper.Player,
                AssignedPosition = Position.GK,
                PositionKey = "GK_2H", // Second half keeper
                IsStarting = false,
                PlannedMinutes = halfGameDuration
            };
            
            squad.PlayerAssignments.Add(gkAssignment);
            Console.WriteLine($"Added GK assignment: {gkAssignment.PositionKey} for {gkAssignment.Player.Name}");

            // Update new goalkeeper's minutes
            newGoalkeeper.Player.MinutesPlayed = halfGameDuration;

            // Remove new goalkeeper from bench
            squad.BenchPlayers.Remove(newGoalkeeper);
        }
        else if (currentGoalkeeper != null)
        {
            Console.WriteLine("No keeper change - current keeper plays full game");
            // No keeper change, current keeper plays full game
            currentGoalkeeper.PlannedMinutes = squad.Config.GameDurationMinutes;
            currentGoalkeeper.Player.MinutesPlayed = squad.Config.GameDurationMinutes;
        }

        // Step 2: Substitute all remaining field players at half-time
        var fieldPlayers = squad.FieldPlayers.Where(fp => fp.IsStarting).ToList();
        var benchPlayersForSubstitution = squad.BenchPlayers.Take(fieldPlayers.Count).ToList();

        Console.WriteLine($"Field players to substitute: {fieldPlayers.Count}");
        Console.WriteLine($"Bench players for substitution: {benchPlayersForSubstitution.Count}");

        // Current starting field players only play first half
        foreach (var fieldPlayer in fieldPlayers)
        {
            fieldPlayer.PlannedMinutes = halfGameDuration;
            fieldPlayer.Player.MinutesPlayed = halfGameDuration;
        }

        // Bring in bench players for second half
        var fieldPositions = _formationPositions.Where(fp => fp.Position != Position.GK).ToArray();
        for (int i = 0; i < benchPlayersForSubstitution.Count && i < fieldPositions.Length; i++)
        {
            var benchPlayer = benchPlayersForSubstitution[i];
            var position = fieldPositions[i];

            // Create assignment for bench player in second half
            var secondHalfAssignment = new PlayerAssignment
            {
                Player = benchPlayer.Player,
                AssignedPosition = position.Position,
                PositionKey = position.Key + "_2H", // Mark as second half
                IsStarting = false,
                PlannedMinutes = halfGameDuration
            };

            squad.PlayerAssignments.Add(secondHalfAssignment);
            Console.WriteLine($"Added 2H assignment: {secondHalfAssignment.PositionKey} for {secondHalfAssignment.Player.Name}");

            // Update player's minutes
            benchPlayer.Player.MinutesPlayed = halfGameDuration;

            // Remove from bench
            squad.BenchPlayers.Remove(benchPlayer);
        }

        // Step 3: Handle remaining bench players (if more bench than field positions)
        // They can share remaining minutes or be tactical substitutions
        var remainingBenchPlayers = squad.BenchPlayers.ToList();
        Console.WriteLine($"Remaining bench players after main substitutions: {remainingBenchPlayers.Count}");
        
        if (remainingBenchPlayers.Any())
        {
            // For remaining bench players, give them minimal playing time
            var minPlayingTime = Math.Min(15, halfGameDuration / 2); // 15 minutes or quarter game
            
            foreach (var benchPlayer in remainingBenchPlayers.Take(3)) // Limit to 3 additional subs
            {
                // Assign to most suitable position
                var bestPosition = fieldPositions
                    .OrderByDescending(fp => benchPlayer.Player.GetPositionScore(fp.Position))
                    .First();

                var subAssignment = new PlayerAssignment
                {
                    Player = benchPlayer.Player,
                    AssignedPosition = bestPosition.Position,
                    PositionKey = bestPosition.Key + "_SUB", // Mark as substitute
                    IsStarting = false,
                    PlannedMinutes = minPlayingTime
                };

                squad.PlayerAssignments.Add(subAssignment);
                Console.WriteLine($"Added SUB assignment: {subAssignment.PositionKey} for {subAssignment.Player.Name}");

                // Update player's minutes
                benchPlayer.Player.MinutesPlayed = minPlayingTime;
            }
        }

        // Update squad statistics after substitutions
        RecalculateSquadStatistics(squad);
        
        Console.WriteLine($"Final PlayerAssignments count: {squad.PlayerAssignments.Count}");
        Console.WriteLine($"Final position keys: {string.Join(", ", squad.PlayerAssignments.Select(pa => pa.PositionKey))}");
    }

    private void RecalculateSquadStatistics(EnhancedSquad squad)
    {
        // Update any cached or calculated properties that depend on player assignments
        // The properties in EnhancedSquad are calculated dynamically, so no action needed here
        // But we could add logging or additional calculations if needed
        
        var totalPlayersUsed = squad.PlayerAssignments.Select(pa => pa.Player).Distinct().Count();
        var totalMinutesPlanned = squad.PlayerAssignments.Sum(pa => pa.PlannedMinutes);
        
        // Log the changes (optional)
        System.Diagnostics.Debug.WriteLine($"Half-time substitutions applied: {totalPlayersUsed} players used, {totalMinutesPlanned} total minutes planned");
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
        if (availableKeepers.Count < config.MinimumGoalkeepers)
        {
            result.ErrorMessages.Add($"Need at least {config.MinimumGoalkeepers} goalkeeper(s), but only {availableKeepers.Count} available");
        }

        // Check if we have enough field players (accounting for goalkeeper)
        var availableFieldPlayers = availableForSelection.Count - config.MinimumGoalkeepers;
        var requiredFieldPlayers = config.PlayersOnField - 1; // -1 for goalkeeper
        if (availableFieldPlayers < requiredFieldPlayers)
        {
            result.ErrorMessages.Add($"Need at least {requiredFieldPlayers} field players, but only {availableFieldPlayers} available after assigning goalkeeper(s)");
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
        // Calculate planned minutes based on strategy and player preferences
        int plannedMinutes = CalculatePlannedMinutes(player, position, config);
        
        return new PlayerAssignment
        {
            Player = player,
            AssignedPosition = position,
            PositionKey = positionKey,
            IsStarting = true,
            PlannedMinutes = plannedMinutes
        };
    }

    private int CalculatePlannedMinutes(Player player, Position position, SquadCreationConfig config)
    {
        // Goalkeepers typically play the full game unless rotation is configured
        if (position == Position.GK)
        {
            return config.AllowGoalkeeperRotation ? config.GameDurationMinutes / 2 : config.GameDurationMinutes;
        }

        // For field players, calculate based on strategy
        return config.Strategy switch
        {
            SquadCreationStrategy.BalancedRotation => Math.Max(config.MinimumPlayingTimeMinutes, config.GameDurationMinutes), // Full game for starting players
            SquadCreationStrategy.OptimalPerformance => config.GameDurationMinutes, // Best players play full game
            SquadCreationStrategy.PreferredPositions => config.GameDurationMinutes, // Starting players play full game
            SquadCreationStrategy.HybridApproach => Math.Max(config.MinimumPlayingTimeMinutes, config.GameDurationMinutes),
            _ => config.GameDurationMinutes // Default to full game for starting XI
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