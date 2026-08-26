using FootballFormation.Core.Reporting;
using Microsoft.AspNetCore.Components.Authorization;

namespace FootballFormation.UI.Pages;

public partial class MatchResult
{
    [Inject] private GameService GameService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private NavigationTrail Trail { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = null!;

    [Parameter]
    public int GameId { get; set; }

    private Game? GameData { get; set; }
    private List<Player>? AllPlayers { get; set; }

    /// The squad of this game's season, for the no-lineup fallback below.
    private SeasonSquad Squad { get; set; } = SeasonSquad.Empty;

    private int? ScoreHome { get; set; }
    private int? ScoreAway { get; set; }

    private int? NewGoalMinute { get; set; }
    private int? NewGoalScorerId { get; set; }
    private int? NewGoalAssisterId { get; set; }
    private bool NewGoalIsOwnGoal { get; set; }

    /// One flag for what the card offers and what GetCommentsAsync fetches: a visitor's page must never contain a private body, and two
    /// sources are how they would drift apart.
    private bool IsAdmin { get; set; }

    /// Null only if the claim is absent.
    private int? CurrentUserId { get; set; }

    private List<GameComment> Comments { get; set; } = [];

    private string? NewCommentBody { get; set; }
    private bool NewCommentIsPublic { get; set; }

    /// Rendered into a hidden element for clipboard.js — the copy button's markup says why even this circuit-carrying page uses a plain
    /// onclick. Null until there is a final score to report.
    private string? SummaryText { get; set; }

    /// /games leaves the link off such a card; this is the same rule for whoever arrives by URL anyway.
    private bool IsFuture => GameData is { } game && game.Date.Date > Time.GetLocalNow().Date;

    /// Logging a goal recounts the score, so it is the same permission as typing a scoreline. Built on <see cref="IsAdmin"/> rather than
    /// an AuthorizeView for the reason given there.
    private bool CanEditScore => IsAdmin && !IsFuture;

    /// Only our side is gated, because the opponent's regular goals are never tracked by scorer.
    private bool AllScorersLogged
    {
        get
        {
            if (GameData is null || ScoreHome is null || ScoreAway is null) return false;
            var ourGoalsLogged = Game.CountOurGoals(GameData.Goals);
            return ourGoalsLogged >= ScoreHome.Value;
        }
    }

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

            // No line-up yet, so fall back to everyone selected for this game.
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

        // The full pool, so anyone who actually appeared stays selectable as a scorer regardless of current membership.
        var playersResult = await PlayerService.GetAllAsync(Cancellation);
        AllPlayers = playersResult.IsSuccess ? playersResult.Value! : [];

        await ReloadComments();
    }

    /// MatchSummaryReport filters privacy again on its own, so an admin's private notes can never reach someone's clipboard whatever
    /// this page loaded.
    private void RefreshSummaryText()
    {
        if (GameData is null || !GameData.HasFinalScore)
        {
            SummaryText = null;
            return;
        }

        var summary = MatchSummaryReport.Build(GameData, Comments);
        SummaryText = MatchSummaryTextBuilder.Build(GameData, summary, L);
    }

    private async Task SaveScore()
    {
        var result = await GameService.SaveScoreAsync(GameId, ScoreHome, ScoreAway);
        if (!Snackbar.Report(L, result, L["Score saved!"])) return;

        // SaveScoreAsync writes straight to the database rather than handing back the row, and the copy button reads GameData rather
        // than the form fields — so without this it would still see the score from before the save.
        if (GameData is not null)
        {
            GameData.ScoreHome = ScoreHome;
            GameData.ScoreAway = ScoreAway;
        }
        RefreshSummaryText();
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

        // Publishing puts the text on the club site, so it is worth a confirmation. Taking it back down is not.
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
        RefreshSummaryText();
    }

    private async Task ReloadGame()
    {
        var gameResult = await GameService.GetByIdAsync(GameId, Cancellation);
        if (gameResult.IsSuccess) GameData = gameResult.Value;
        RefreshSummaryText();
    }

    private void ResetGoalForm()
    {
        NewGoalMinute = null;
        NewGoalScorerId = null;
        NewGoalAssisterId = null;
        NewGoalIsOwnGoal = false;
    }
}
