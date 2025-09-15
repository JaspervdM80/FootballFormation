using Microsoft.AspNetCore.Components;
using FootballFormation.UI.Models;
using FootballFormation.UI.Services;
using FootballFormation.UI.Enums;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FootballFormation.UI.Components.Pages;

public partial class SquadCreator
{
    private bool _dataLoaded = false;
    private bool _isLoading = false;
    private string _statusMessage = string.Empty;
    private List<PlayerAvailability> _playerAvailabilities = [];
    private List<EnhancedSquad> _createdSquads = [];
    private SquadCreationConfig _config = new()
    {
        Strategy = SquadCreationStrategy.PreferredPositions,
        GameDurationMinutes = 60,
        PlayersOnField = 11,
        MinimumPlayingTimeMinutes = 15,
        AllowPositionFlexibility = true
    };

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task LoadSampleData()
    {
        _isLoading = true;
        _statusMessage = "Loading sample players...";
        StateHasChanged();

        try
        {
            var players = await HttpClient.GetFromJsonAsync<List<Player>>("sample-players.json", _jsonSerializerOptions);
            
            if (players != null && players.Any())
            {
                _playerAvailabilities = PlayerAvailabilityService.CreateFromPlayers(players);
                _dataLoaded = true;
                _statusMessage = $"Successfully loaded {players.Count} players!";
                
                // Clear status message after a delay
                await Task.Delay(2000);
                _statusMessage = string.Empty;
                StateHasChanged();
            }
            else
            {
                _statusMessage = "No players found in sample data.";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading sample data: {ex.Message}");
            _statusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task CreateSingleSquad()
    {
        _isLoading = true;
        _statusMessage = "Creating optimal squad...";
        StateHasChanged();

        try
        {
            await Task.Delay(100); // Small delay for UI feedback
            var squad = SquadCreationService.CreateSquad(_playerAvailabilities, _config);
            _createdSquads = [squad];
            _statusMessage = "Squad created successfully!";
            
            // Clear status message after delay
            await Task.Delay(2000);
            _statusMessage = string.Empty;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating squad: {ex.Message}");
            _statusMessage = $"Error creating squad: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task CreateMultipleSquads()
    {
        _isLoading = true;
        _statusMessage = "Generating multiple squad options...";
        StateHasChanged();

        try
        {
            await Task.Delay(100); // Small delay for UI feedback
            _createdSquads = SquadCreationService.CreateMultipleSquads(_playerAvailabilities, _config, 3);
            _statusMessage = $"Generated {_createdSquads.Count} squad options!";
            
            // Clear status message after delay
            await Task.Delay(2000);
            _statusMessage = string.Empty;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating squads: {ex.Message}");
            _statusMessage = $"Error creating squads: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
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

    private Dictionary<string, Player?> GetPositionedPlayersDict(EnhancedSquad squad)
    {
        var dict = new Dictionary<string, Player?>();
        
        foreach (var assignment in squad.PlayerAssignments)
        {
            dict[assignment.PositionKey] = assignment.Player;
        }

        return dict;
    }

    private int GetAvailablePlayersCount()
    {
        return _playerAvailabilities.Count(pa => !pa.IsAbsent);
    }

    private int GetGoalkeeperCount()
    {
        return _playerAvailabilities.Count(pa => pa.IsKeeper && !pa.IsAbsent);
    }

    private RenderFragment<(Player? player, string position, bool isKeeper)> RenderSquadPlayer => context => builder =>
    {
        var (player, position, isKeeper) = context;
        
        builder.OpenElement(0, "div");

        if (player != null)
        {
            // Parse position to Position enum for scoring calculation
            Position positionEnum = ParsePositionFromString(position);
            
            // Find the assignment from the current context (we need to pass squad context)
            var assignment = GetPlayerAssignmentForPosition(player, position);
            
            var cssClass = isKeeper ? "player keeper" : "player";
            if (assignment?.IsPreferredPosition == true || 
                (assignment == null && (player.MainPosition == positionEnum || player.SecondaryPositions.Contains(positionEnum))))
            {
                cssClass += " preferred";
            }
            
            builder.AddAttribute(1, "class", cssClass);

            // Calculate the actual position-specific rating
            double rating;
            if (assignment != null)
            {
                rating = assignment.PositionMatchQuality;
            }
            else
            {
                // Calculate position score directly if assignment not found
                rating = player.GetPositionScore(positionEnum);
            }

            // Enhanced tooltip with more information
            var tooltip = $"{player.Name}\nPositie: {position}\nSterkte: {player.Skills.AverageSkill:F1}\n" +
                         $"Hoofd positie: {player.MainPosition}\nPositie score: {rating:F1}";
            
            if (player.SecondaryPositions?.Any() == true)
            {
                tooltip += $"\nExtra posities: {string.Join(", ", player.SecondaryPositions)}";
            }

            if (player.MainPosition == positionEnum || player.SecondaryPositions.Contains(positionEnum))
            {
                tooltip += "\n? Preferred positie!";
            }
            
            builder.AddAttribute(2, "title", tooltip);

            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "class", "player-name");
            builder.AddContent(5, GetTruncatedName(player.Name));
            builder.CloseElement();

            builder.OpenElement(6, "div");
            builder.AddAttribute(7, "class", "player-position");
            builder.AddContent(8, position);
            builder.CloseElement();

            builder.OpenElement(9, "div");
            builder.AddAttribute(10, "class", "player-rating");
            builder.OpenElement(11, "i");
            builder.AddAttribute(12, "class", "bi bi-star-fill");
            builder.CloseElement();
            builder.AddContent(13, $" {rating:F1}");
            builder.CloseElement();
        }
        else
        {
            builder.AddAttribute(12, "class", isKeeper ? "player keeper" : "player");
            builder.AddAttribute(13, "title", $"Geen speler toegewezen aan {position}");
            
            builder.OpenElement(14, "div");
            builder.AddAttribute(15, "class", "player-name");
            builder.AddContent(16, "-");
            builder.CloseElement();
            
            builder.OpenElement(17, "div");
            builder.AddAttribute(18, "class", "player-position");
            builder.AddContent(19, position);
            builder.CloseElement();
        }

        builder.CloseElement();
    };

    private PlayerAssignment? GetPlayerAssignmentForPosition(Player player, string position)
    {
        // Look through all created squads to find the assignment
        // This could be improved by passing squad context, but for now this works
        foreach (var squad in _createdSquads)
        {
            var assignment = squad.PlayerAssignments.FirstOrDefault(pa => 
                pa.Player == player && pa.PositionKey == position);
            if (assignment != null)
                return assignment;
        }
        return null;
    }

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

    private static string GetTruncatedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "-";
            
        // If name is too long, show first name + first letter of last name
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && name.Length > 10)
        {
            return $"{parts[0]} {parts[1][0]}. ";
        }
        
        // If single name is too long, truncate
        if (name.Length > 12)
        {
            return name[..9] + "...";
        }
        
        return name;
    }
}