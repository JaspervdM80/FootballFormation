using FootballFormation.Core.Models;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

public partial class PitchView
{
    [Parameter, EditorRequired]
    public FormationType Formation { get; set; }

    [Parameter, EditorRequired]
    public List<GamePlayerPosition> Positions { get; set; } = [];

    [Parameter]
    public int? DraggedPlayerId { get; set; }

    [Parameter]
    public EventCallback<PlayerPosition> OnPlayerDropped { get; set; }

    [Parameter]
    public EventCallback<PlayerPosition> OnPlayerRemoved { get; set; }

    [Parameter]
    public EventCallback<PlayerPosition> OnPlayerDragStart { get; set; }

    private static string GetChipCssClass(PlayerPosition position, Player player)
    {
        var baseClass = "player-circle";
        var fit = PositionFitHelper.GetFit(player, position);
        return fit switch
        {
            PositionFit.Preferred => $"{baseClass} chip-preferred",
            PositionFit.NaturalFit => $"{baseClass} chip-natural",
            PositionFit.Alternative => $"{baseClass} chip-alternative",
            PositionFit.Compatible => $"{baseClass} chip-compatible",
            _ => $"{baseClass} chip-out-of-position"
        };
    }

    private static string GetSlotStyle(PlayerPosition position)
    {
        var (left, top) = PitchPositionHelper.GetCoordinates(position);
        return $"left: {left}%; top: {top}%;";
    }
}
