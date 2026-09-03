namespace FootballFormation.UI.Components;

public partial class PlayerList
{
    [Parameter, EditorRequired]
    public List<Player> Players { get; set; } = [];

    [Parameter]
    public EventCallback<int> OnDragStart { get; set; }

    [Parameter]
    public bool ReadOnly { get; set; }
}
