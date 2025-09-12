using FootballFormation.Enums;
using FootballFormation.Models;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.Components.Pages;

public partial class Home
{
    private bool _dataLoaded = false;
    private string _jsonInput = string.Empty;

    private RenderFragment RenderPlayer(Player? player, string position, bool isKeeper = false)
    {
        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", isKeeper ? "player keeper" : "player");

            if (player != null)
            {
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
                builder.AddContent(10, player.Skills.AverageSkill.ToString("F1"));
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(11, "-");
            }

            builder.CloseElement();
        };
    }

    private void LoadSampleData()
    {
        var players = new List<Player>
        {
            new() {
                Name = "Lieke",
                PreferredPositions = [Position.CB, Position.LB, Position.ST],
                Skills = new PlayerSkills { Attacking = 3, Midfield = 2, Defense = 5, Passing = 3, Speed = 5, Shooting = 3, Insight = 3, Fierceness = 5 }
            },
            new() {
                Name = "Fiene",
                PreferredPositions = [Position.CAM, Position.RW, Position.LW],
                Skills = new PlayerSkills { Attacking = 4, Midfield = 3, Defense = 2, Passing = 3, Speed = 4, Shooting = 4, Insight = 2, Fierceness = 2 }
            },
            new() {
                Name = "Feline",
                PreferredPositions = [Position.CB],
                IsKeeper = true,
                Skills = new PlayerSkills { Attacking = 1, Midfield = 2, Defense = 5, Passing = 4, Speed = 2, Shooting = 1, Insight = 3, Fierceness = 3 }
            },
            new() {
                Name = "Fenna",
                PreferredPositions = [Position.CAM, Position.ST],
                Skills = new PlayerSkills { Attacking = 3, Midfield = 3, Defense = 2, Passing = 3, Speed = 2, Shooting = 2, Insight = 4, Fierceness = 1 }
            },
            new() {
                Name = "Roos",
                PreferredPositions = [Position.CB, Position.LB, Position.RB],
                IsAbsent = true,
                Skills = new PlayerSkills { Attacking = 2, Midfield = 2, Defense = 4, Passing = 2, Speed = 4, Shooting = 1, Insight = 2, Fierceness = 4 }
            },
            new() {
                Name = "Elena",
                PreferredPositions = [Position.CB, Position.LB, Position.RB],
                Skills = new PlayerSkills { Attacking = 1, Midfield = 1, Defense = 2, Passing = 1, Speed = 1, Shooting = 1, Insight = 1, Fierceness = 1 }
            },
            new() {
                Name = "Suus",
                PreferredPositions = [Position.ST],
                Skills = new PlayerSkills { Attacking = 4, Midfield = 2, Defense = 2, Passing = 2, Speed = 3, Shooting = 3, Insight = 3, Fierceness = 4 }
            },
            new() {
                Name = "Lise",
                PreferredPositions = [Position.CAM],
                Skills = new PlayerSkills { Attacking = 2, Midfield = 3, Defense = 1, Passing = 4, Speed = 1, Shooting = 2, Insight = 3, Fierceness = 1 }
            },
            new() {
                Name = "Fleur",
                PreferredPositions = [Position.CAM, Position.ST],
                IsKeeper = true,
                Skills = new PlayerSkills { Attacking = 3, Midfield = 3, Defense = 2, Passing = 3, Speed = 4, Shooting = 2, Insight = 3, Fierceness = 4 }
            },
            new() {
                Name = "Suze",
                PreferredPositions = [Position.CB],
                Skills = new PlayerSkills { Attacking = 1, Midfield = 2, Defense = 4, Passing = 2, Speed = 2, Shooting = 1, Insight = 2, Fierceness = 4 }
            },
            new() {
                Name = "Kim",
                PreferredPositions = [Position.CAM, Position.CDM],
                Skills = new PlayerSkills { Attacking = 3, Midfield = 4, Defense = 2, Passing = 4, Speed = 3, Shooting = 4, Insight = 3, Fierceness = 2 }
            },
            new() {
                Name = "Liv",
                PreferredPositions = [Position.CAM, Position.RB, Position.LB],
                Skills = new PlayerSkills { Attacking = 2, Midfield = 3, Defense = 3, Passing = 3, Speed = 3, Shooting = 3, Insight = 3, Fierceness = 2 }
            },
            new() {
                Name = "Lotte",
                PreferredPositions = [Position.CB, Position.CAM],
                Skills = new PlayerSkills { Attacking = 1, Midfield = 2, Defense = 2, Passing = 1, Speed = 1, Shooting = 1, Insight = 1, Fierceness = 1 }
            },
            new() {
                Name = "Julia",
                PreferredPositions = [Position.CAM],
                Skills = new PlayerSkills { Attacking = 1, Midfield = 1, Defense = 1, Passing = 1, Speed = 1, Shooting = 1, Insight = 1, Fierceness = 1 }
            },
            new() {
                Name = "Jula",
                PreferredPositions = [Position.CAM],
                Skills = new PlayerSkills { Attacking = 4, Midfield = 5, Defense = 1, Passing = 5, Speed = 3, Shooting = 4, Insight = 5, Fierceness = 1 }
            }
        };

        FormationService.LoadPlayers(players);
        _dataLoaded = true;
    }

    private void LoadJsonData()
    {
        try
        {
            var players = System.Text.Json.JsonSerializer.Deserialize<List<Player>>(_jsonInput);
            if (players != null && players.Any())
            {
                FormationService.LoadPlayers(players);
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
    }
}
