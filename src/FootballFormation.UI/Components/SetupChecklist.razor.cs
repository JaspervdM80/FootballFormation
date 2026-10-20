namespace FootballFormation.UI.Components;

public partial class SetupChecklist
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter, EditorRequired] public TeamSetup Setup { get; set; } = null!;

    [Parameter, EditorRequired] public Team Team { get; set; } = null!;

    [Parameter] public EventCallback OnHide { get; set; }

    private string TeamLinkUrl => AppRoutes.TeamLinkUrl(Navigation.BaseUri, Team);

    private sealed record Step(bool Done, string Href, string Title, string Body);

    private IEnumerable<Step> Steps =>
    [
        new(Setup.HasMatchSettings, AppRoutes.Preferences, L["Check the match settings"],
            L["Game length, halves or quarters, formation and match day."]),
        new(Setup.HasSquad, AppRoutes.Players, L["Add the squad"],
            L["Add your players, or pick them from the club."]),
        new(Setup.HasTrainings, AppRoutes.Settings, L["Set the training schedule"],
            L["Pick the training days and the first and last training, and the evenings fill themselves in."]),
        new(Setup.HasGames, AppRoutes.Games, L["Add the first games"],
            L["Opponent, date and venue for each match."])
    ];
}
