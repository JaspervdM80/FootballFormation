using Microsoft.AspNetCore.Components;
using FootballFormation.UI.Enums;

namespace FootballFormation.UI.Components.Shared;

public partial class FormationDisplay
{
    [Parameter] public string FormationTitle { get; set; } = "";
    [Parameter] public double TeamStrength { get; set; }
    [Parameter] public Player? GoalkeeperPlayer { get; set; }
    [Parameter] public Dictionary<string, Player?> PositionedPlayers { get; set; } = new();
    [Parameter] public List<Player> BenchPlayers { get; set; } = [];
    [Parameter] public RenderFragment<(Player? player, string position, bool isKeeper)> PlayerTemplate { get; set; } = default!;

    private Player? GetPlayerInPosition(string position)
    {
        PositionedPlayers.TryGetValue(position, out var player);
        return player;
    }

    private RenderFragment RenderPlayer(Player? player, string position, bool isKeeper = false)
    {
        if (PlayerTemplate != null)
        {
            return PlayerTemplate((player, position, isKeeper));
        }

        // Enhanced default template with position-specific rating
        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", isKeeper ? "player keeper" : "player");

            if (player != null)
            {
                // Calculate position-specific rating instead of just average skill
                var positionEnum = ParsePositionFromString(position);
                var positionRating = player.GetPositionScore(positionEnum);
                
                // Add tooltip with detailed player information including position rating
                var tooltip = $"{player.Name}\nPositie: {position}\nSterkte: {player.Skills.AverageSkill:F1}\n" +
                             $"Positie score: {positionRating:F1}\nHoofd positie: {player.MainPosition}";
                
                if (player.SecondaryPositions?.Any() == true)
                {
                    tooltip += $"\nExtra posities: {string.Join(", ", player.SecondaryPositions)}";
                }

                if (player.MainPosition == positionEnum || player.SecondaryPositions.Contains(positionEnum))
                {
                    tooltip += "\n⭐ Preferred positie!";
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
                // Use position-specific rating instead of average skill
                builder.AddContent(13, $" {positionRating:F1}");
                builder.CloseElement();
            }
            else
            {
                builder.AddAttribute(12, "title", $"Geen speler toegewezen aan {position}");
                builder.OpenElement(13, "div");
                builder.AddAttribute(14, "class", "player-name");
                builder.AddContent(15, "-");
                builder.CloseElement();
                
                builder.OpenElement(16, "div");
                builder.AddAttribute(17, "class", "player-position");
                builder.AddContent(18, position);
                builder.CloseElement();
            }

            builder.CloseElement();
        };
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
            return $"{parts[0]} {parts[1].Substring(0, 1)}.";
        }
        
        // If single name is too long, truncate
        if (name.Length > 12)
        {
            return name.Substring(0, 9) + "...";
        }
        
        return name;
    }
}
