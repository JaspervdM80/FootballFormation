using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

public partial class MatchResult
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }

    /// <summary>The squad of this game's season, for the no-lineup fallback below.</summary>
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;

    private int? ScoreHome { get; set; }
    private int? ScoreAway { get; set; }

    // New goal form
    private int? NewGoalMinute { get; set; }
    private int? NewGoalScorerId { get; set; }
    private int? NewGoalAssisterId { get; set; }
    private bool NewGoalIsOwnGoal { get; set; }

    /// <summary>
    /// Decides both what the comments card offers and — via <c>GetCommentsAsync</c> — which comments
    /// are fetched at all. Deliberately one flag for both: a visitor's page must never contain a
    /// private body, and reading the two from separate sources is how they would drift apart.
    /// </summary>
    private bool IsAdmin { get; set; }

    /// <summary>The id written onto comments this visitor creates. Null only if the claim is absent.</summary>
    private int? CurrentUserId { get; set; }

    private List<GameComment> Comments { get; set; } = [];

    // New comment form
    private string? NewCommentBody { get; set; }
    private bool NewCommentIsPublic { get; set; }

    /// <summary>
    /// True when both scores are set AND every goal our team scored has a named scorer.
    /// Opponent's regular goals aren't tracked, so we only gate on our side. Used to hide
    /// the "add scorer" form once there's nothing left to add.
    /// </summary>
    private bool AllScorersLogged
    {
        get
        {
            if (GameData is null || ScoreHome is null || ScoreAway is null) return false;
            var ourGoalsLogged = Game.CountOurGoals(GameData.Goals);
            return ourGoalsLogged >= ScoreHome.Value;
        }
    }

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
                return GameData.SelectRoster(AllPlayers, Squad);

            return AllPlayers.Where(p => involvedIds.Contains(p.Id)).ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateTask;
        IsAdmin = authState.User.IsAdmin();
        CurrentUserId = authState.User.UserId();

        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);

        // Not a missing game — the visitor left. Redirecting would move them again.
        if (gameResult.IsCancelled) return;

        if (!Snackbar.ReportFailure(L, gameResult))
        {
            Trail.Redirect(AppRoutes.Games);
            return;
        }

        GameData = gameResult.Value!;
        ScoreHome = GameData.ScoreHome;
        ScoreAway = GameData.ScoreAway;

        var squadResult = await SquadService.GetSquadAsync(GameData.SeasonId, Cancellation);
        Squad = squadResult.IsSuccess ? squadResult.Value! : SeasonSquad.Empty;

        // Anyone who actually appeared stays selectable as a scorer regardless of current
        // membership, so the full pool is still loaded.
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];

        await ReloadComments();
    }

    private async Task SaveScore()
    {
        var result = await GameService.SaveScoreAsync(GameId, ScoreHome, ScoreAway);
        Snackbar.Report(L, result, L["Score saved!"]);
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
        if (!Snackbar.Report(L, result, L["Goal added!"])) return;

        await ReloadGame();
        ResetGoalForm();
    }

    private async Task RemoveGoal(GameGoal goal)
    {
        var result = await GameService.RemoveGoalAsync(goal.Id);
        if (!Snackbar.Report(L, result, L["Goal removed"], Severity.Warning)) return;

        await ReloadGame();
    }

    private async Task AddComment()
    {
        if (string.IsNullOrWhiteSpace(NewCommentBody)) return;

        var comment = new GameComment
        {
            GameId = GameId,
            Body = NewCommentBody.Trim(),
            IsPublic = NewCommentIsPublic,
            AuthorId = CurrentUserId
        };

        var result = await GameService.AddCommentAsync(comment);
        if (!Snackbar.Report(L, result, L["Comment added"])) return;

        NewCommentBody = null;
        NewCommentIsPublic = false;
        await ReloadComments();
    }

    private async Task ToggleCommentVisibility(GameComment comment)
    {
        var makePublic = !comment.IsPublic;

        // Publishing puts the text on the club site, so it is worth a confirmation. Unpublishing
        // takes it back down and needs none.
        if (makePublic && !await DialogService.ConfirmAsync(
                L["Publish comment"],
                L["This comment becomes visible to everyone who opens this match. Continue?"],
                "Publish"))
        {
            return;
        }

        var result = await GameService.UpdateCommentAsync(comment.Id, comment.Body, makePublic);
        if (!Snackbar.Report(L, result, makePublic ? L["Comment published"] : L["Comment is now admin only"])) return;

        await ReloadComments();
    }

    private async Task RemoveComment(GameComment comment)
    {
        if (!await DialogService.ConfirmDeleteAsync(
                L["Delete comment"],
                L["Delete this comment? This cannot be undone."]))
        {
            return;
        }

        var result = await GameService.RemoveCommentAsync(comment.Id);
        if (!Snackbar.Report(L, result, L["Comment removed"], Severity.Warning)) return;

        await ReloadComments();
    }

    private async Task ReloadComments()
    {
        var result = await GameService.GetCommentsAsync(GameId, includePrivate: IsAdmin, Cancellation);
        Comments = result.IsSuccess ? result.Value! : [];
    }

    private async Task ReloadGame()
    {
        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);
        if (gameResult.IsSuccess) GameData = gameResult.Value;
    }

    private void ResetGoalForm()
    {
        NewGoalMinute = null;
        NewGoalScorerId = null;
        NewGoalAssisterId = null;
        NewGoalIsOwnGoal = false;
    }
}
