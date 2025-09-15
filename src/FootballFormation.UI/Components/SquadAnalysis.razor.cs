using Microsoft.AspNetCore.Components;
using FootballFormation.UI.Enums;
using FootballFormation.UI.Models;

namespace FootballFormation.UI.Components;

public partial class SquadAnalysis
{
    [Parameter] public EnhancedSquad? Squad { get; set; }
    [Parameter] public List<PlayerAvailability>? AvailableGoalkeepers { get; set; }

    // Player time tracking model
    public class PlayerTimeInfo
    {
        public Player Player { get; set; } = null!;
        public int FirstHalfMinutes { get; set; }
        public int SecondHalfMinutes { get; set; }
        public int TotalMinutes => FirstHalfMinutes + SecondHalfMinutes;
        public bool IsGoalkeeper { get; set; }
    }

    // Substitution tracking model
    public class SubstitutionInfo
    {
        public string PlayerOut { get; set; } = string.Empty;
        public string PlayerIn { get; set; } = string.Empty;
        public bool IsGoalkeeperChange { get; set; }
    }

    private int GetTotalPlannedMinutes()
    {
        return Squad?.PlayerAssignments?.Sum(pa => pa.PlannedMinutes) ?? 0;
    }

    private double GetAverageMinutes()
    {
        if (Squad?.PlayerAssignments?.Any() != true) return 0;
        return (double)GetTotalPlannedMinutes() / Squad.PlayerAssignments.Count;
    }

    private string GetPositionCssClass(Position position)
    {
        return position switch
        {
            Position.GK => "goalkeeper",
            Position.DC or Position.DL or Position.DR => "defender",
            Position.CDM or Position.CAM => "midfielder",
            Position.LW or Position.RW or Position.ST => "attacker",
            _ => "default"
        };
    }

    private string GetStrategyDisplayName(SquadCreationStrategy strategy)
    {
        return strategy switch
        {
            SquadCreationStrategy.PreferredPositions => "Preferred Positions",
            SquadCreationStrategy.OptimalPerformance => "Optimal Performance",
            SquadCreationStrategy.BalancedRotation => "Balanced Rotation",
            SquadCreationStrategy.HybridApproach => "Hybrid Approach",
            _ => strategy.ToString()
        };
    }

    private bool HasHalfTimeSubstitutions()
    {
        if (Squad?.PlayerAssignments == null) return false;
        
        // Check if any player has a position key indicating second half
        return Squad.PlayerAssignments.Any(pa => 
            pa.PositionKey.EndsWith("_2H") || 
            pa.PositionKey.EndsWith("_SUB"));
    }

    private List<PlayerAssignment> GetFirstHalfPlayers()
    {
        if (Squad?.PlayerAssignments == null) return [];
        
        return Squad.PlayerAssignments
            .Where(pa => !pa.PositionKey.EndsWith("_2H") && !pa.PositionKey.EndsWith("_SUB"))
            .ToList();
    }

    private List<PlayerAssignment> GetSecondHalfPlayers()
    {
        if (Squad?.PlayerAssignments == null) return [];
        
        return Squad.PlayerAssignments
            .Where(pa => pa.PositionKey.EndsWith("_2H") || pa.PositionKey.EndsWith("_SUB"))
            .ToList();
    }

    private List<PlayerAssignment> GetAllPlayers()
    {
        if (Squad?.PlayerAssignments == null) return [];
        return Squad.PlayerAssignments.ToList();
    }

    private List<Player> GetAllUniquePlayersUsed()
    {
        if (Squad?.PlayerAssignments == null) return [];
        return Squad.PlayerAssignments.Select(pa => pa.Player).Distinct().ToList();
    }

    private int GetMinutesPlayedInFirstHalf()
    {
        return GetFirstHalfPlayers().Sum(pa => pa.PlannedMinutes);
    }

    private int GetMinutesPlayedInSecondHalf()
    {
        return GetSecondHalfPlayers().Sum(pa => pa.PlannedMinutes);
    }

    private double GetFirstHalfAverageRating()
    {
        var firstHalfPlayers = GetFirstHalfPlayers();
        return firstHalfPlayers.Any() ? firstHalfPlayers.Average(pa => pa.PositionMatchQuality) : 0;
    }

    private double GetSecondHalfAverageRating()
    {
        var secondHalfPlayers = GetSecondHalfPlayers();
        return secondHalfPlayers.Any() ? secondHalfPlayers.Average(pa => pa.PositionMatchQuality) : 0;
    }

    private double GetOverallAverageRating()
    {
        var allPlayers = GetAllPlayers();
        return allPlayers.Any() ? allPlayers.Average(pa => pa.PositionMatchQuality) : 0;
    }

    private int GetSubstitutionCount()
    {
        return GetSecondHalfPlayers().Count;
    }

    private int GetGoalkeeperChangeCount()
    {
        var firstHalfGoalkeepers = GetFirstHalfPlayers().Where(pa => pa.AssignedPosition == Position.GK).ToList();
        var secondHalfGoalkeepers = GetSecondHalfPlayers().Where(pa => pa.AssignedPosition == Position.GK).ToList();
        
        // If there are different goalkeepers in each half, there was a change
        if (firstHalfGoalkeepers.Any() && secondHalfGoalkeepers.Any())
        {
            var firstHalfGK = firstHalfGoalkeepers.First().Player;
            var secondHalfGK = secondHalfGoalkeepers.First().Player;
            return firstHalfGK != secondHalfGK ? 1 : 0;
        }
        
        return 0;
    }

    private string GetCleanPositionKey(string positionKey)
    {
        return positionKey.Replace("_2H", "").Replace("_SUB", "");
    }

    private int GetPositionOrder(PlayerAssignment assignment)
    {
        // Order positions: GK, Defense, Midfield, Attack
        return assignment.AssignedPosition switch
        {
            Position.GK => 1,
            Position.DL => 2,
            Position.DC => 3,
            Position.DR => 4,
            Position.CDM => 5,
            Position.CAM => 6,
            Position.LW => 7,
            Position.ST => 8,
            Position.RW => 9,
            _ => 10
        };
    }

    // Methods for FormationDisplay integration
    private Player? GetFirstHalfGoalkeeper()
    {
        return GetFirstHalfPlayers().FirstOrDefault(pa => pa.AssignedPosition == Position.GK)?.Player;
    }

    private Player? GetSecondHalfGoalkeeper()
    {
        return GetSecondHalfPlayers().FirstOrDefault(pa => pa.AssignedPosition == Position.GK)?.Player;
    }

    private Dictionary<string, Player?> GetFirstHalfPositionedPlayers()
    {
        var dict = new Dictionary<string, Player?>();
        var firstHalfPlayers = GetFirstHalfPlayers();
        
        foreach (var assignment in firstHalfPlayers.Where(pa => pa.AssignedPosition != Position.GK))
        {
            var cleanKey = GetCleanPositionKey(assignment.PositionKey);
            dict[cleanKey] = assignment.Player;
        }
        
        return dict;
    }

    private Dictionary<string, Player?> GetSecondHalfPositionedPlayers()
    {
        var dict = new Dictionary<string, Player?>();
        var secondHalfPlayers = GetSecondHalfPlayers();
        
        foreach (var assignment in secondHalfPlayers.Where(pa => pa.AssignedPosition != Position.GK))
        {
            var cleanKey = GetCleanPositionKey(assignment.PositionKey);
            dict[cleanKey] = assignment.Player;
        }
        
        return dict;
    }

    private Dictionary<string, Player?> GetInitialPositionedPlayers()
    {
        var dict = new Dictionary<string, Player?>();
        
        if (Squad?.PlayerAssignments != null)
        {
            foreach (var assignment in Squad.PlayerAssignments.Where(pa => pa.AssignedPosition != Position.GK))
            {
                dict[assignment.PositionKey] = assignment.Player;
            }
        }
        
        return dict;
    }

    // New methods for playing time analysis
    private List<PlayerTimeInfo> GetAllPlayersWithTime()
    {
        if (Squad?.PlayerAssignments == null) return [];

        var playerTimes = new Dictionary<Player, PlayerTimeInfo>();

        // Process first half players
        foreach (var assignment in GetFirstHalfPlayers())
        {
            if (!playerTimes.ContainsKey(assignment.Player))
            {
                playerTimes[assignment.Player] = new PlayerTimeInfo 
                { 
                    Player = assignment.Player,
                    IsGoalkeeper = assignment.AssignedPosition == Position.GK
                };
            }
            playerTimes[assignment.Player].FirstHalfMinutes = assignment.PlannedMinutes;
        }

        // Process second half players
        foreach (var assignment in GetSecondHalfPlayers())
        {
            if (!playerTimes.ContainsKey(assignment.Player))
            {
                playerTimes[assignment.Player] = new PlayerTimeInfo 
                { 
                    Player = assignment.Player,
                    IsGoalkeeper = assignment.AssignedPosition == Position.GK
                };
            }
            playerTimes[assignment.Player].SecondHalfMinutes = assignment.PlannedMinutes;
        }

        return playerTimes.Values.ToList();
    }

    private List<SubstitutionInfo> GetSubstitutions()
    {
        var substitutions = new List<SubstitutionInfo>();
        
        if (Squad?.PlayerAssignments == null) return substitutions;

        var firstHalfPlayers = GetFirstHalfPlayers();
        var secondHalfPlayers = GetSecondHalfPlayers();

        // Check goalkeeper changes
        var firstHalfGK = firstHalfPlayers.FirstOrDefault(pa => pa.AssignedPosition == Position.GK);
        var secondHalfGK = secondHalfPlayers.FirstOrDefault(pa => pa.AssignedPosition == Position.GK);
        
        if (firstHalfGK != null && secondHalfGK != null && firstHalfGK.Player != secondHalfGK.Player)
        {
            substitutions.Add(new SubstitutionInfo
            {
                PlayerOut = firstHalfGK.Player.Name,
                PlayerIn = secondHalfGK.Player.Name,
                IsGoalkeeperChange = true
            });
        }

        // Find field player substitutions by comparing positions
        var firstHalfFieldPlayers = firstHalfPlayers.Where(pa => pa.AssignedPosition != Position.GK).ToList();
        var secondHalfFieldPlayers = secondHalfPlayers.Where(pa => pa.AssignedPosition != Position.GK).ToList();

        // Group by clean position key to find substitutions
        var positionGroups = firstHalfFieldPlayers
            .Select(pa => GetCleanPositionKey(pa.PositionKey))
            .Distinct();

        foreach (var position in positionGroups)
        {
            var firstHalfPlayer = firstHalfFieldPlayers.FirstOrDefault(pa => GetCleanPositionKey(pa.PositionKey) == position);
            var secondHalfPlayer = secondHalfFieldPlayers.FirstOrDefault(pa => GetCleanPositionKey(pa.PositionKey) == position);

            if (firstHalfPlayer != null && secondHalfPlayer != null && firstHalfPlayer.Player != secondHalfPlayer.Player)
            {
                substitutions.Add(new SubstitutionInfo
                {
                    PlayerOut = firstHalfPlayer.Player.Name,
                    PlayerIn = secondHalfPlayer.Player.Name,
                    IsGoalkeeperChange = false
                });
            }
        }

        return substitutions;
    }

    private int GetTotalGameMinutes()
    {
        return Squad?.Config?.GameDurationMinutes ?? 60;
    }

    private double GetAveragePlayingTime()
    {
        var players = GetAllPlayersWithTime();
        return players.Any() ? players.Average(p => p.TotalMinutes) : 0;
    }

    private double GetPlayingTimeBalance()
    {
        var players = GetAllPlayersWithTime();
        if (!players.Any()) return 0;

        var times = players.Select(p => p.TotalMinutes).ToList();
        var average = times.Average();
        var variance = times.Sum(t => Math.Pow(t - average, 2)) / times.Count;
        
        // Return balance score (lower is better, scale 0-10)
        return Math.Max(0, 10 - Math.Sqrt(variance) / 3);
    }

    // Helper methods for player rendering
    private Position ParsePositionFromString(string positionString)
    {
        return positionString switch
        {
            "GK" => Position.GK,
            "DC1" or "DC2" or "DC" => Position.DC,
            "DL" => Position.DL,
            "DR" => Position.DR,
            "CDM1" or "CDM2" or "CDM" => Position.CDM,
            "CAM" => Position.CAM,
            "LW" => Position.LW,
            "ST" => Position.ST,
            "RW" => Position.RW,
            _ => Position.None
        };
    }

    private PlayerAssignment? GetPlayerAssignmentForPosition(Player player, string position)
    {
        if (Squad?.PlayerAssignments == null) return null;
        
        return Squad.PlayerAssignments.FirstOrDefault(pa => 
            pa.Player == player && 
            (pa.PositionKey == position || GetCleanPositionKey(pa.PositionKey) == position));
    }

    private static string GetTruncatedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "-";
            
        // For compact view, be more aggressive with truncation
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && name.Length > 8)
        {
            return $"{parts[0]} {parts[1][0]}.";
        }
        
        if (name.Length > 10)
        {
            return name[..7] + "...";
        }
        
        return name;
    }

    // RenderFragment for players with time display
    private RenderFragment<(Player? player, string position, bool isKeeper)> RenderPlayerWithTime => context => builder =>
    {
        var (player, position, isKeeper) = context;
        
        builder.OpenElement(0, "div");

        if (player != null)
        {
            Position positionEnum = ParsePositionFromString(position);
            var assignment = GetPlayerAssignmentForPosition(player, position);
            var playerTime = GetAllPlayersWithTime().FirstOrDefault(pt => pt.Player == player);
            
            var cssClass = isKeeper ? "player keeper with-time" : "player with-time";
            if (assignment?.IsPreferredPosition == true || 
                (assignment == null && (player.MainPosition == positionEnum || player.SecondaryPositions.Contains(positionEnum))))
            {
                cssClass += " preferred";
            }
            
            builder.AddAttribute(1, "class", cssClass);

            double rating = assignment?.PositionMatchQuality ?? player.GetPositionScore(positionEnum);
            
            var tooltip = $"{player.Name}\nPosition: {position}\nRating: {rating:F1}";
            if (playerTime != null)
            {
                tooltip += $"\nTotal: {playerTime.TotalMinutes} min";
                if (playerTime.FirstHalfMinutes > 0) tooltip += $"\n1st Half: {playerTime.FirstHalfMinutes} min";
                if (playerTime.SecondHalfMinutes > 0) tooltip += $"\n2nd Half: {playerTime.SecondHalfMinutes} min";
            }
            
            builder.AddAttribute(2, "title", tooltip);

            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "class", "player-name-display");
            builder.AddContent(5, GetTruncatedName(player.Name));
            builder.CloseElement();

            builder.OpenElement(6, "div");
            builder.AddAttribute(7, "class", "player-rating-display");
            builder.AddContent(8, rating.ToString("F1"));
            builder.CloseElement();

            // Add time display
            if (playerTime != null && playerTime.TotalMinutes > 0)
            {
                builder.OpenElement(9, "div");
                builder.AddAttribute(10, "class", "player-time-display");
                builder.AddContent(11, $"{playerTime.TotalMinutes}'");
                builder.CloseElement();
            }
        }
        else
        {
            builder.AddAttribute(12, "class", isKeeper ? "player keeper with-time empty" : "player with-time empty");
            builder.AddAttribute(13, "title", $"No player assigned to {position}");
            
            builder.OpenElement(14, "div");
            builder.AddAttribute(15, "class", "player-name-display");
            builder.AddContent(16, "-");
            builder.CloseElement();
            
            builder.OpenElement(17, "div");
            builder.AddAttribute(18, "class", "player-rating-display");
            builder.AddContent(19, "-");
            builder.CloseElement();
        }

        builder.CloseElement();
    };
}
