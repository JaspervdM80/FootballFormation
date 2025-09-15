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