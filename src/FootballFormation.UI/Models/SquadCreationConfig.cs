namespace FootballFormation.UI.Models;

/// <summary>
/// Configuration for squad creation
/// </summary>
public class SquadCreationConfig
{
    public int GameDurationMinutes { get; set; } = 60;
    public int PlayersOnField { get; set; } = 11; // Including goalkeeper
    public int MinimumPlayingTimeMinutes { get; set; } = 15;
    public bool AllowPositionFlexibility { get; set; } = true;
    public SquadCreationStrategy Strategy { get; set; } = SquadCreationStrategy.BalancedRotation;
    
    /// <summary>
    /// Whether to allow goalkeeper rotation during the game
    /// </summary>
    public bool AllowGoalkeeperRotation { get; set; } = true;
    
    /// <summary>
    /// Minimum number of goalkeepers required
    /// </summary>
    public int MinimumGoalkeepers { get; set; } = 1;
    
    /// <summary>
    /// Maximum number of goalkeepers to include in squad
    /// </summary>
    public int MaximumGoalkeepers { get; set; } = 2;

    /// <summary>
    /// Whether to automatically apply half-time substitutions to bring in all bench players
    /// </summary>
    public bool EnableHalfTimeSubstitutions { get; set; } = true;

    /// <summary>
    /// Whether to force goalkeeper change at half-time if multiple keepers are available
    /// </summary>
    public bool ForceGoalkeeperChangeAtHalfTime { get; set; } = true;
}

/// <summary>
/// Different strategies for creating squads
/// </summary>
public enum SquadCreationStrategy
{
    /// <summary>
    /// Prioritize players' main positions
    /// </summary>
    PreferredPositions,
    
    /// <summary>
    /// Balance playing time across all players
    /// </summary>
    BalancedRotation,
    
    /// <summary>
    /// Focus on team strength and best position matches
    /// </summary>
    OptimalPerformance,
    
    /// <summary>
    /// Mix of position preference and balanced rotation
    /// </summary>
    HybridApproach
}