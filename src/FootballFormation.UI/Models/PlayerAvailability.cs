using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Models;

/// <summary>
/// Represents the availability and role information for a player during squad creation
/// </summary>
public class PlayerAvailability
{
    public Player Player { get; set; } = null!;
    public bool IsKeeper { get; set; } = false;
    public bool IsAbsent { get; set; } = false;
    public int TargetMinutes { get; set; } = 45; // Default target playing time
    public int Priority { get; set; } = 1; // 1 = highest priority, higher numbers = lower priority
    
    /// <summary>
    /// Gets whether this player can play as goalkeeper based on their positions and keeper status
    /// </summary>
    public bool CanPlayKeeper => IsKeeper || Player.MainPosition == Position.GK || Player.SecondaryPositions.Contains(Position.GK);
    
    /// <summary>
    /// Gets whether this player is available for selection
    /// </summary>
    public bool IsAvailable => !IsAbsent;
}