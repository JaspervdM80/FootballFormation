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
    public EventCallback<int> OnPlayerDropped { get; set; }

    [Parameter]
    public EventCallback<int> OnPlayerRemoved { get; set; }

    [Parameter]
    public EventCallback<int> OnPlayerDragStart { get; set; }

    private PlayerPosition[] AllSlots => [PlayerPosition.GK, .. Formation.DefaultPositions()];

    /// <summary>
    /// Matches GamePlayerPositions to formation slots.
    /// Uses SlotIndex as the source of truth; falls back to position matching for legacy data.
    /// </summary>
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
