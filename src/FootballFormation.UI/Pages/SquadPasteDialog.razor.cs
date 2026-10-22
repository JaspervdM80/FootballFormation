namespace FootballFormation.UI.Pages;

/// Hands back the pasted text rather than the plan: the service re-plans against the squad as it is when the list is saved.
public partial class SquadPasteDialog
{
    // Proper names are data rather than copy, so the example is the same in either language.
    private const string Example = "7 Sanne de Vries\nLotte\n12 Femke Jansen";

    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter, EditorRequired]
    public List<Player> SquadPlayers { get; set; } = [];

    [Parameter, EditorRequired]
    public List<Player> EarlierPlayers { get; set; } = [];

    private string? _text;
    private IReadOnlyList<PastedSquadEntry> _plan = [];

    private string? Text
    {
        get => _text;
        set
        {
            _text = value;
            _plan = PastedSquadList.Plan(PastedSquadList.Parse(value), SquadPlayers, EarlierPlayers);
        }
    }

    private int Count(PastedLineAction action) => _plan.Count(e => e.Action == action);

    private int ToAdd => Count(PastedLineAction.Create) + Count(PastedLineAction.AddExisting);

    private bool CanAdd => ToAdd > 0 && Count(PastedLineAction.Invalid) == 0;

    private string Summary =>
        Count(PastedLineAction.Invalid) is > 0 and var invalid
            ? invalid == 1
                ? L["1 line needs fixing before anyone can be added."]
                : L["{0} lines need fixing before anyone can be added.", invalid]
            : ToAdd == 0
                ? L["Everyone on the list is already in the squad."]
                : L["{0} new, {1} from an earlier season, {2} already in the squad.",
                    Count(PastedLineAction.Create), Count(PastedLineAction.AddExisting), Count(PastedLineAction.AlreadyInSquad)];

    private static int? ShirtShown(PastedSquadEntry entry) =>
        entry.Existing is null ? entry.Line.ShirtNumber : entry.Existing.ShirtNumber;

    private static string StatusClass(PastedSquadEntry entry) => entry.Action switch
    {
        PastedLineAction.Create => "paste-create",
        PastedLineAction.AddExisting => "paste-existing",
        PastedLineAction.AlreadyInSquad => "paste-skipped",
        _ => "paste-invalid"
    };

    private string StatusText(PastedSquadEntry entry) => entry.Action switch
    {
        PastedLineAction.Create => L["New player"],
        PastedLineAction.AddExisting => L["From an earlier season"],
        PastedLineAction.AlreadyInSquad => L["Already in the squad, skipped"],
        _ => entry.Line.Problem switch
        {
            PastedLineProblem.MissingName => L["No name"],
            PastedLineProblem.ShirtNumberOutOfRange =>
                L["Shirt number must be {0} to {1}", PastedSquadList.MinShirtNumber, PastedSquadList.MaxShirtNumber],
            PastedLineProblem.DuplicateShirtNumber => L["Shirt number listed twice"],
            _ => L["Name listed twice"]
        }
    };

    private void Submit()
    {
        if (!CanAdd) return;
        MudDialog.Close(DialogResult.Ok(Text!));
    }

    private void Cancel() => MudDialog.Cancel();
}
