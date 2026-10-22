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
    private int _new, _earlier, _skipped, _invalid;

    private string? Text
    {
        get => _text;
        set
        {
            _text = value;
            _plan = PastedSquadList.Plan(value, SquadPlayers, EarlierPlayers);
            _invalid = _plan.Count(e => !e.IsValid);
            _new = _plan.Count(e => e.IsValid && e.Action == PastedLineAction.Create);
            _earlier = _plan.Count(e => e.IsValid && e.Action == PastedLineAction.AddExisting);
            _skipped = _plan.Count(e => e.IsValid && e.Action == PastedLineAction.AlreadyInSquad);
        }
    }

    private int ToAdd => _new + _earlier;

    private bool CanAdd => ToAdd > 0 && _invalid == 0;

    private string Summary =>
        _invalid == 1 ? L["1 line needs fixing before anyone can be added."]
        : _invalid > 1 ? L["{0} lines need fixing before anyone can be added.", _invalid]
        : ToAdd == 0 ? L["Everyone on the list is already in the squad."]
        : L["{0} new, {1} from an earlier season, {2} already in the squad.", _new, _earlier, _skipped];

    private static string StatusClass(PastedSquadEntry entry) =>
        !entry.IsValid ? "paste-invalid"
        : entry.Action switch
        {
            PastedLineAction.Create => "paste-create",
            PastedLineAction.AddExisting => "paste-existing",
            _ => "paste-skipped"
        };

    private string StatusText(PastedSquadEntry entry) => entry.Problem switch
    {
        PastedLineProblem.MissingName => L["No name"],
        PastedLineProblem.ShirtNumberOutOfRange =>
            L["Shirt number must be {0} to {1}", PastedSquadList.MinShirtNumber, PastedSquadList.MaxShirtNumber],
        PastedLineProblem.DuplicateShirtNumber => L["Shirt number listed twice"],
        PastedLineProblem.DuplicateName => L["Name listed twice"],
        _ => entry.Action switch
        {
            PastedLineAction.Create => L["New player"],
            PastedLineAction.AddExisting => L["From an earlier season"],
            _ => L["Already in the squad, skipped"]
        }
    };

    private void Submit()
    {
        if (!CanAdd) return;
        MudDialog.Close(DialogResult.Ok(Text!));
    }

    private void Cancel() => MudDialog.Cancel();
}
