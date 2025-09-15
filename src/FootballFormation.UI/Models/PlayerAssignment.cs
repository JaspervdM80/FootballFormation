using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Models;

/// <summary>
/// Represents a player's assignment in a specific formation period with playing time tracking
/// </summary>
public class PlayerAssignment
{
    public Player Player { get; set; } = null!;
    public Position AssignedPosition { get; set; }
    public string PositionKey { get; set; } = string.Empty; // e.g., "DC1", "ST", "GK"
    public bool IsStarting { get; set; } = true;
    public int MinutesPlayed { get; set; } = 0;
    public int PlannedMinutes { get; set; } = 0;
    
    /// <summary>
    /// Gets the position match quality (how well the player fits the assigned position)
    /// </summary>
    public double PositionMatchQuality => Player.GetPositionScore(AssignedPosition);
    
    /// <summary>
    /// Gets whether the player is playing their preferred position (main or secondary)
    /// </summary>
    public bool IsPreferredPosition => Player.MainPosition == AssignedPosition || 
                                       Player.SecondaryPositions.Contains(AssignedPosition);
}