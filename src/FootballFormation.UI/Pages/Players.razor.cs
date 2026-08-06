using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using FootballFormation.UI.Helpers;
using FootballFormation.UI.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace FootballFormation.UI.Pages;

/// <summary>
/// Season-scoped squad management. The squad is authoritative — it decides who can be picked for
/// this season's games — so the page follows the season picker rather than listing everyone on file.
/// </summary>
public partial class Players
{
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private SeasonSquadService SquadService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private SeasonSquad? _squad;
    private Season? _previousSeason;
    private bool _loaded;

    private string SeasonName => SeasonState.SelectedSeason?.Name ?? "";

    protected override async Task LoadAsync()
    {
        _loaded = false;
        _squad = null;
        _previousSeason = null;

        // "All seasons" has no squad to edit — a squad belongs to exactly one season.
        if (SeasonId is not { } seasonId)
        {
            _loaded = true;
            return;
        }

        var squadResult = await SquadService.GetSquadAsync(seasonId);
        _squad = Snackbar.ReportFailure(L, squadResult) ? squadResult.Value! : SeasonSquad.Empty;

        var previousResult = await SquadService.FindPreviousSeasonAsync(seasonId);
        if (previousResult.IsSuccess) _previousSeason = previousResult.Value;

        _loaded = true;
    }

    private void OnRowClicked(TableRowClickEventArgs<SeasonSquadMember> args)
    {
        if (args.Item is not null)
            Navigation.NavigateTo(AppRoutes.PlayerStats(args.Item.PlayerId));
    }

    private void ShowCurrentSeason()
    {
        var current = SeasonState.Seasons.FirstOrDefault(s => s.IsCurrent) ?? SeasonState.Seasons.FirstOrDefault();
        if (current is not null) SeasonState.Select(current.Id);
    }

    private async Task CopyPreviousSquad()
    {
        if (SeasonId is not { } seasonId || _previousSeason is null) return;

        var result = await SquadService.CopyFromAsync(_previousSeason.Id, seasonId);
        Snackbar.Report(L, result, L["{0} players copied from {1}", result.Value, _previousSeason.Name]);
        await LoadAsync();
    }

    /// <summary>Creates a new person and puts them in this season's squad in one action — creating
    /// someone you then have to add separately is never what you meant.</summary>
    private async Task AddNewPlayer()
    {
        if (SeasonId is not { } seasonId) return;

        var player = await ShowPlayerDialogAsync(L["Add Player"]);
        if (player is null) return;

        var created = await PlayerService.CreateAsync(player);
        if (!Snackbar.ReportFailure(L, created)) return;

        var added = await SquadService.AddMemberAsync(seasonId, created.Value!.Id);
        Snackbar.Report(L, added, L["{0} added to the squad", player.DisplayName]);
        await LoadAsync();
    }

    /// <summary>Adds someone already on file — a player from an earlier season, or a guest being
    /// promoted into this season's squad.</summary>
    private async Task AddExistingPlayer()
    {
        if (SeasonId is not { } seasonId) return;

        var candidatesResult = await SquadService.GetNonMembersAsync(seasonId);
        if (!Snackbar.ReportFailure(L, candidatesResult)) return;

        var candidates = candidatesResult.Value!;
        if (candidates.Count == 0)
        {
            Snackbar.Add(L["Everyone on file is already in this squad."], Severity.Info);
            return;
        }

        var picked = await ShowSquadMemberDialogAsync(candidates);
        if (picked is not { } choice) return;

        var result = await SquadService.AddMemberAsync(seasonId, choice.PlayerId, choice.IsGuest);
        Snackbar.Report(L, result, L["{0} added to the squad",
            candidates.First(p => p.Id == choice.PlayerId).DisplayName]);
        await LoadAsync();
    }

    private async Task ToggleGuest(SeasonSquadMember member)
    {
        if (SeasonId is not { } seasonId) return;

        var name = member.Player!.DisplayName;
        var result = await SquadService.SetGuestAsync(seasonId, member.PlayerId, !member.IsGuest);
        Snackbar.Report(L, result, member.IsGuest
            ? L["{0} is now a squad player", name]
            : L["{0} is now a guest", name]);
        await LoadAsync();
    }

    /// <summary>Removing from a squad is the everyday action; the service refuses once the player
    /// has minutes or goals that season, so history is never silently rewritten.</summary>
    private async Task RemoveMember(SeasonSquadMember member)
    {
        if (SeasonId is not { } seasonId) return;

        var name = member.Player!.DisplayName;
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Remove from squad"],
            L["Remove {0} from the {1} squad? Their history stays intact.", name, SeasonName]);
        if (!confirmed) return;

        var result = await SquadService.RemoveMemberAsync(seasonId, member.PlayerId);
        Snackbar.Report(L, result, L["{0} removed from the squad", name], Severity.Warning);
        await LoadAsync();
    }

    private async Task OpenEditDialog(Player player)
    {
        var updated = await ShowPlayerDialogAsync(L["Edit Player"], player);
        if (updated is null) return;

        var result = await PlayerService.UpdateAsync(updated);
        Snackbar.Report(L, result, L["Player {0} updated", updated.DisplayName]);
        await LoadAsync();
    }

    /// <summary>Deletes the person everywhere, cascading their lineup and goal rows in every
    /// season. Rare and destructive — removing from a squad is almost always what is wanted.</summary>
    private async Task DeletePlayer(Player player)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Player"],
            L["Are you sure you want to delete {0}?", player.DisplayName]);
        if (!confirmed) return;

        var result = await PlayerService.DeleteAsync(player.Id);
        Snackbar.Report(L, result, L["Player {0} deleted", player.DisplayName], Severity.Warning);
        await LoadAsync();
    }

    /// <summary>Returns the edited player, or null when the dialog was cancelled.</summary>
    private async Task<Player?> ShowPlayerDialogAsync(string title, Player? player = null)
    {
        return await DialogService.PromptAsync<PlayerDialog, Player>(title, p =>
        {
            if (player is not null) p.Add(x => x.Player, player);
        });
    }

    /// <summary>Returns the picked player and guest status, or null when cancelled.</summary>
    private async Task<SquadMemberChoice?> ShowSquadMemberDialogAsync(List<Player> candidates)
    {
        return await DialogService.PromptAsync<SquadMemberDialog, SquadMemberChoice>(
            L["Add to squad"], p => p.Add(x => x.Candidates, candidates));
    }
}
