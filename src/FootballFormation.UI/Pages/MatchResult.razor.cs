using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class MatchResult
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

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

            // If no lineup yet, fall back to everyone selected for this game
            if (involvedIds.Count == 0)
                return GameData.SelectRoster(AllPlayers);

            return AllPlayers.Where(p => involvedIds.Contains(p.Id)).ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (!Snackbar.ReportFailure(gameResult))
        {
            Navigation.NavigateTo("/games");
            return;
        }

        GameData = gameResult.Value!;
        ScoreHome = GameData.ScoreHome;
        ScoreAway = GameData.ScoreAway;

        var playersResult = await PlayerService.GetAllAsync();
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];
    }

    private async Task SaveScore()
    {
        var result = await GameService.SaveScoreAsync(GameId, ScoreHome, ScoreAway);
        Snackbar.Report(result, "Score saved!");
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
        if (!Snackbar.Report(result, "Goal added!")) return;

        await ReloadGame();
        ResetGoalForm();
    }

    private async Task RemoveGoal(GameGoal goal)
    {
        var result = await GameService.RemoveGoalAsync(goal.Id);
        if (!Snackbar.Report(result, "Goal removed", Severity.Warning)) return;

        await ReloadGame();
    }

    private async Task ReloadGame()
    {
        var gameResult = await GameService.GetByIdAsync(GameId);
        if (gameResult.IsSuccess) GameData = gameResult.Value;
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
