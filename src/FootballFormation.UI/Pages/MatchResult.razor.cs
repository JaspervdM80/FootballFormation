using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class MatchResult
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ILogger<MatchResult> Logger { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }

    private int? ScoreHome { get; set; }
    private int? ScoreAway { get; set; }

    // New goal form
    private int? NewGoalMinute { get; set; }
    private int? NewGoalScorerId { get; set; }
    private int? NewGoalAssisterId { get; set; }
    private bool NewGoalIsOwnGoal { get; set; }

    /// <summary>
    /// Players involved in this game (starters + subs across all periods).
    /// </summary>
    private List<Player> SquadPlayers
    {
        get
        {
            if (AllPlayers is null || GameData is null) return [];

            var involvedIds = GameData.Periods
                .SelectMany(p => p.PlayerPositions)
                .Select(pp => pp.PlayerId)
                .Distinct()
                .ToHashSet();

            // If no lineup yet, show all players
            if (involvedIds.Count == 0)
                return AllPlayers;

            return AllPlayers.Where(p => involvedIds.Contains(p.Id)).ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (gameResult.IsFailure || gameResult.Value is null)
        {
            Snackbar.Add("Game not found", Severity.Error);
            Navigation.NavigateTo("/games");
            return;
        }

        GameData = gameResult.Value;
        ScoreHome = GameData.ScoreHome;
        ScoreAway = GameData.ScoreAway;

        var playersResult = await PlayerService.GetAllAsync();
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];
    }

    private async Task SaveScore()
    {
        var result = await GameService.SaveScoreAsync(GameId, ScoreHome, ScoreAway);
        if (result.IsSuccess)
        {
            Snackbar.Add("Score saved!", Severity.Success);
        }
        else
        {
            Snackbar.Add(result.Error!, Severity.Error);
        }
    }

    private async Task AddGoal()
    {
        if (NewGoalScorerId is null) return;

        var goal = new GameGoal
        {
            GameId = GameId,
            ScorerId = NewGoalScorerId.Value,
            AssisterId = NewGoalAssisterId,
            Minute = NewGoalMinute,
            IsOwnGoal = NewGoalIsOwnGoal
        };

        var result = await GameService.AddGoalAsync(goal);
        if (result.IsSuccess)
        {
            Snackbar.Add("Goal added!", Severity.Success);
            await ReloadGame();
            ResetGoalForm();
        }
        else
        {
            Snackbar.Add(result.Error!, Severity.Error);
        }
    }

    private async Task RemoveGoal(GameGoal goal)
    {
        var result = await GameService.RemoveGoalAsync(goal.Id);
        if (result.IsSuccess)
        {
            Snackbar.Add("Goal removed", Severity.Warning);
            await ReloadGame();
        }
        else
        {
            Snackbar.Add(result.Error!, Severity.Error);
        }
    }

    private async Task ReloadGame()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (gameResult.IsSuccess && gameResult.Value is not null)
        {
            GameData = gameResult.Value;
        }
    }

    private void ResetGoalForm()
    {
        NewGoalMinute = null;
        NewGoalScorerId = null;
        NewGoalAssisterId = null;
        NewGoalIsOwnGoal = false;
    }

    private void NavigateBack() => Navigation.NavigateTo("/games");
}
