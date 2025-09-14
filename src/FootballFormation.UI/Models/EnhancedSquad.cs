using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Models;

/// <summary>
/// Enhanced squad model that includes playing time and position assignments
/// </summary>
public class EnhancedSquad
{
    public int SquadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<PlayerAssignment> PlayerAssignments { get; set; } = [];
    public List<PlayerAvailability> BenchPlayers { get; set; } = [];
    public SquadCreationConfig Config { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Gets all starting players (those assigned to positions)
    /// </summary>
    public List<PlayerAssignment> StartingPlayers => PlayerAssignments.Where(pa => pa.IsStarting).ToList();
    
    /// <summary>
    /// Gets the goalkeeper assignment
    /// </summary>
    public PlayerAssignment? Goalkeeper => PlayerAssignments.FirstOrDefault(pa => pa.AssignedPosition == Position.GK);
    
    /// <summary>
    /// Gets all field player assignments
    /// </summary>
    public List<PlayerAssignment> FieldPlayers => PlayerAssignments.Where(pa => pa.AssignedPosition != Position.GK).ToList();
    
    /// <summary>
    /// Calculates the overall squad strength based on position matches
    /// </summary>
    public double OverallStrength => PlayerAssignments.Average(pa => pa.PositionMatchQuality);
    
    /// <summary>
    /// Gets the percentage of players playing in their preferred positions
    /// </summary>
    public double PreferredPositionPercentage => 
        (double)PlayerAssignments.Count(pa => pa.IsPreferredPosition) / PlayerAssignments.Count * 100;
}