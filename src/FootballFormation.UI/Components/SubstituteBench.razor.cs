using FootballFormation.Core.Models;
using Microsoft.AspNetCore.Components;

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
    public EventCallback OnPlayerDroppedToSub { get; set; }
}
