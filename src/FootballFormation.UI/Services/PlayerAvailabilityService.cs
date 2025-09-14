using FootballFormation.UI.Models;
using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Services;

/// <summary>
/// Helper service to create PlayerAvailability objects from various inputs
/// </summary>
public interface IPlayerAvailabilityService
{
    /// <summary>
    /// Creates PlayerAvailability from a list of players with default settings
    /// </summary>
    List<PlayerAvailability> CreateFromPlayers(List<Player> players);
    
    /// <summary>
    /// Creates PlayerAvailability with specified keeper and absent players
    /// </summary>
    List<PlayerAvailability> CreateFromPlayers(List<Player> players, List<string> keeperNames, List<string> absentPlayerNames);
    
    /// <summary>
    /// Creates PlayerAvailability with detailed configuration
    /// </summary>
    List<PlayerAvailability> CreateFromPlayersDetailed(List<Player> players, Dictionary<string, PlayerGameInfo> playerGameInfo);
}

public class PlayerAvailabilityService : IPlayerAvailabilityService
{
    public List<PlayerAvailability> CreateFromPlayers(List<Player> players)
    {
        return players.Select(player => new PlayerAvailability
        {
            Player = player,
            IsKeeper = player.MainPosition == Position.GK || player.SecondaryPositions.Contains(Position.GK),
            IsAbsent = false,
            TargetMinutes = 30, // Default target
            Priority = 1 // Default priority
        }).ToList();
    }

    public List<PlayerAvailability> CreateFromPlayers(List<Player> players, List<string> keeperNames, List<string> absentPlayerNames)
    {
        return players.Select(player => new PlayerAvailability
        {
            Player = player,
            IsKeeper = keeperNames.Contains(player.Name, StringComparer.OrdinalIgnoreCase) || 
                      player.MainPosition == Position.GK || 
                      player.SecondaryPositions.Contains(Position.GK),
            IsAbsent = absentPlayerNames.Contains(player.Name, StringComparer.OrdinalIgnoreCase),
            TargetMinutes = 30,
            Priority = 1
        }).ToList();
    }

    public List<PlayerAvailability> CreateFromPlayersDetailed(List<Player> players, Dictionary<string, PlayerGameInfo> playerGameInfo)
    {
        return players.Select(player =>
        {
            var gameInfo = playerGameInfo.GetValueOrDefault(player.Name, new PlayerGameInfo());
            
            return new PlayerAvailability
            {
                Player = player,
                IsKeeper = gameInfo.IsKeeper ?? (player.MainPosition == Position.GK || player.SecondaryPositions.Contains(Position.GK)),
                IsAbsent = gameInfo.IsAbsent ?? false,
                TargetMinutes = gameInfo.TargetMinutes ?? 30,
                Priority = gameInfo.Priority ?? 1
            };
        }).ToList();
    }
}

/// <summary>
/// Information about a player's game participation
/// </summary>
public class PlayerGameInfo
{
    public bool? IsKeeper { get; set; }
    public bool? IsAbsent { get; set; }
    public int? TargetMinutes { get; set; }
    public int? Priority { get; set; }
}