using FootballFormation.Core.Models;
using FootballFormation.UI.Helpers;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

public partial class PitchOverview
{
    [Parameter, EditorRequired]
    public FormationType Formation { get; set; }

    [Parameter, EditorRequired]
    public List<GamePlayerPosition> Positions { get; set; } = [];

    private static string GetChipCssClass(PlayerPosition position, Player player)
    {
        var baseClass = "po-player";
        var fit = PositionFitHelper.GetFit(player, position);
        return fit switch
        {
            PositionFit.Preferred => $"{baseClass} po-preferred",
            PositionFit.NaturalFit => $"{baseClass} po-natural",
            PositionFit.Alternative => $"{baseClass} po-alternative",
            PositionFit.Compatible => $"{baseClass} po-compatible",
            _ => $"{baseClass} po-out-of-position"
        };
    }

    private static string GetSlotStyle(PlayerPosition position)
    {
        var (left, top) = PitchPositionHelper.GetCoordinates(position);
        return $"left: {left}%; top: {top}%;";
    }
}
