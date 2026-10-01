namespace FootballFormation.UI.Components;

public partial class SubstituteBench
{
    [Parameter, EditorRequired]
    public List<GamePlayerPosition> Lineup { get; set; } = [];

    [Parameter]
    public EventCallback<GamePlayerPosition> OnSubRemoved { get; set; }

    [Parameter]
    public int? DraggedPlayerId { get; set; }

    [Parameter]
    public int? SelectedPlayerId { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }

    [Parameter]
    public EventCallback OnPlayerDroppedToSub { get; set; }

    [Parameter]
    public EventCallback<int> OnSubDragStart { get; set; }

    [Parameter]
    public EventCallback<int> OnSwapWithSub { get; set; }

    [Parameter]
    public EventCallback<int> OnSubTapped { get; set; }

    /// A tap on the panel itself rather than on one of its subs.
    [Parameter]
    public EventCallback OnBenchTapped { get; set; }

    private Task OnSubClicked(int playerId) =>
        ReadOnly ? Task.CompletedTask : OnSubTapped.InvokeAsync(playerId);

    private Task OnBenchClicked() =>
        ReadOnly ? Task.CompletedTask : OnBenchTapped.InvokeAsync();
}
