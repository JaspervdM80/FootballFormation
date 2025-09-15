using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using FootballFormation.UI.Models;
using FootballFormation.UI.Services;
using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Components.Pages;

public partial class Home
{
    [Inject] private HttpClient HttpClient { get; set; } = default!;
    [Inject] private IOptions<JsonSerializerOptions> JsonOptions { get; set; } = default!;
    [Inject] private IFormationService FormationService { get; set; } = default!;
    [Inject] private IGameSetupService GameSetupService { get; set; } = default!;
    
    private bool _dataLoaded = false;
    private string _jsonInput = string.Empty;
    private bool _useMultipleSetups = false;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private RenderFragment<(Player? player, string position, bool isKeeper)> RenderHomePlayer => context => builder =>
    {
        var (player, position, isKeeper) = context;
        
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", isKeeper ? "player keeper" : "player");

        if (player != null)
        {
            // Calculate position-specific rating
            var positionEnum = ParsePositionFromString(position);
            var positionRating = player.GetPositionScore(positionEnum);

            builder.OpenElement(2, "div");
            builder.AddAttribute(3, "class", "player-name");
            builder.AddContent(4, player.Name);
            builder.CloseElement();

            builder.OpenElement(5, "div");
            builder.AddAttribute(6, "class", "player-position");
            builder.AddContent(7, position);
            builder.CloseElement();

            builder.OpenElement(8, "div");
            builder.AddAttribute(9, "class", "player-rating");
            // Show position-specific rating instead of average skill
            builder.AddContent(10, positionRating.ToString("F1"));
            builder.CloseElement();
        }
        else
        {
            builder.AddContent(11, "-");
        }

        builder.CloseElement();
    };

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

    private Dictionary<string, Player?> GetFormationPositionedPlayers(Formation formation)
    {
        var dict = new Dictionary<string, Player?>();
        
        // Map the formation positions to our standard position keys
        dict["LW"] = formation.PositionedPlayers.GetValueOrDefault("LW");
        dict["ST"] = formation.PositionedPlayers.GetValueOrDefault("ST");
        dict["RW"] = formation.PositionedPlayers.GetValueOrDefault("RW");
        dict["CAM"] = formation.PositionedPlayers.GetValueOrDefault("CAM");
        dict["CDM1"] = formation.PositionedPlayers.GetValueOrDefault("CDM1");
        dict["CDM2"] = formation.PositionedPlayers.GetValueOrDefault("CDM2");
        dict["DL"] = formation.PositionedPlayers.GetValueOrDefault("DL") ?? formation.PositionedPlayers.GetValueOrDefault("LB");
        dict["DC1"] = formation.PositionedPlayers.GetValueOrDefault("DC1") ?? formation.PositionedPlayers.GetValueOrDefault("CB1");
        dict["DC2"] = formation.PositionedPlayers.GetValueOrDefault("DC2") ?? formation.PositionedPlayers.GetValueOrDefault("CB2");
        dict["DR"] = formation.PositionedPlayers.GetValueOrDefault("DR") ?? formation.PositionedPlayers.GetValueOrDefault("RB");

        return dict;
    }

    private async Task LoadSampleData()
    {
        try
        {
            var players = await HttpClient.GetFromJsonAsync<List<Player>>("sample-players.json", _jsonSerializerOptions);
            
            if (players != null && players.Any())
            {
                if (_useMultipleSetups)
                {
                    GameSetupService.GenerateGameSetups(players);
                }
                else
                {
                    FormationService.LoadPlayers(players);
                }
                _dataLoaded = true;
            }
        }
        catch (Exception ex)
        {
            // Handle error - in real app show error message
            Console.WriteLine($"Error loading sample data: {ex.Message}");
        }
    }

    private void LoadJsonData()
    {
        try
        {
            var players = System.Text.Json.JsonSerializer.Deserialize<List<Player>>(_jsonInput, _jsonSerializerOptions);
            if (players != null && players.Any())
            {
                if (_useMultipleSetups)
                {
                    GameSetupService.GenerateGameSetups(players);
                }
                else
                {
                    FormationService.LoadPlayers(players);
                }
                _dataLoaded = true;
            }
        }
        catch (Exception ex)
        {
            // Handle error - in real app show error message
            Console.WriteLine($"Error loading JSON: {ex.Message}");
        }
    }

    private void ResetData()
    {
        _dataLoaded = false;
        _jsonInput = string.Empty;
        _useMultipleSetups = false;
    }

    private void SelectGameSetup(int setupId)
    {
        GameSetupService.SelectGameSetup(setupId);
        StateHasChanged();
    }

    private void ToggleSetupMode()
    {
        _useMultipleSetups = !_useMultipleSetups;
        if (_dataLoaded)
        {
            // Regenerate with new mode if data is already loaded
            if (_useMultipleSetups)
            {
                var players = FormationService.Players.ToList();
                GameSetupService.GenerateGameSetups(players);
            }
            else
            {
                var players = GameSetupService.SelectedGameSetup?.Formations
                    .SelectMany(f => f.PositionedPlayers.Values.Concat(new[] { f.Goalkeeper }))
                    .Where(p => p != null)
                    .Distinct()
                    .ToList() ?? [];

                if (players.Any())
                {
                    FormationService.LoadPlayers(players);
                }
            }
        }
    }

    private List<Player> GetAllPlayersFromSetup(GameSetup setup)
    {
        var allPlayers = new HashSet<Player>();

        foreach (var formation in setup.Formations)
        {
            foreach (var player in formation.PositionedPlayers.Values.Where(p => p != null))
            {
                allPlayers.Add(player!);
            }

            if (formation.Goalkeeper != null)
            {
                allPlayers.Add(formation.Goalkeeper);
            }

            foreach (var benchPlayer in formation.Bench)
            {
                allPlayers.Add(benchPlayer);
            }
        }

        return allPlayers.Where(p => !p.IsAbsent).ToList();
    }
}
