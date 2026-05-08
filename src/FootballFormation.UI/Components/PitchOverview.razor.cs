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

    private PlayerPosition[] AllSlots => [PlayerPosition.GK, .. Formation.DefaultPositions()];

    private GamePlayerPosition?[] BuildSlotAssignments()
    {
        var slots = AllSlots;
        var assignments = new GamePlayerPosition?[slots.Length];
        var starters = Positions.Where(p => !p.IsSubstitute).ToList();

        // Pass 1: direct slot index assignment
        foreach (var entry in starters.Where(p => p.SlotIndex is not null).ToList())
        {
            var idx = entry.SlotIndex!.Value;
            if (idx >= 0 && idx < slots.Length && assignments[idx] is null)
            {
                assignments[idx] = entry;
                starters.Remove(entry);
            }
        }

        // Pass 2: position-based fallback for legacy data without SlotIndex
        for (int i = 0; i < slots.Length; i++)
        {
            if (assignments[i] is not null) continue;
            var match = starters.FirstOrDefault(p => p.Position == slots[i]);
            if (match is not null)
            {
                assignments[i] = match;
                starters.Remove(match);
            }
        }
        return assignments;
    }

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

    private string GetSlotStyle(int slotIndex)
    {
        var slots = AllSlots;
        var pos = slots[slotIndex];
        var count = slots.Count(p => p == pos);
        var index = 0;
        for (int j = 0; j < slotIndex; j++)
        {
            if (slots[j] == pos) index++;
        }

        var (left, top) = PitchPositionHelper.GetCoordinates(pos, index, count);
        return $"left: {left}%; top: {top}%;";
    }
}
