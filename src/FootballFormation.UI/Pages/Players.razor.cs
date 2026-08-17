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

        var squadResult = await SquadService.GetSquadAsync(seasonId, Cancellation);
        _squad = Snackbar.ReportFailure(L, squadResult) ? squadResult.Value! : SeasonSquad.Empty;

        var previousResult = await SquadService.FindPreviousSeasonAsync(seasonId, Cancellation);
        if (previousResult.IsSuccess) _previousSeason = previousResult.Value;

        _loaded = true;
    }

    private void OnRowClicked(TableRowClickEventArgs<SeasonSquadMember> args)
    {
        if (args.Item is not null)
            Navigation.NavigateTo(AppRoutes.PlayerStats(args.Item.PlayerId));
    }

    private async Task ShowCurrentSeason()
    {
        var current = SeasonState.Seasons.FirstOrDefault(s => s.IsCurrent) ?? SeasonState.Seasons.FirstOrDefault();
        if (current is not null) await SeasonState.SelectAsync(current.Id);
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

        var edited = await ShowPlayerDialogAsync(L["Add Player"]);
        if (edited is null) return;

        var created = await PlayerService.CreateAsync(edited.Player);
        if (!Snackbar.ReportFailure(L, created)) return;

        var added = await SquadService.AddMemberAsync(seasonId, created.Value!.Id, edited.IsGuest);
        Snackbar.Report(L, added, L["{0} added to the squad", edited.Player.DisplayName]);
        await LoadAsync();
    }

    /// <summary>Adds someone already on file — a player from an earlier season, or a guest being
    /// promoted into this season's squad.</summary>
    private async Task AddExistingPlayer()
    {
        if (SeasonId is not { } seasonId) return;

        var candidatesResult = await SquadService.GetNonMembersAsync(seasonId, Cancellation);
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

    /// <summary>
    /// Edits the person and their place in this season's squad in one dialog. They are two writes
    /// because they are two things — a player is edited once and is a guest in one season and not
    /// in another — and the guest one is only made when the switch actually moved, so an ordinary
    /// name change does not touch the squad.
    /// </summary>
    private async Task OpenEditDialog(SeasonSquadMember member)
    {
        if (SeasonId is not { } seasonId) return;

        var edited = await ShowPlayerDialogAsync(L["Edit Player"], member.Player!, member.IsGuest);
        if (edited is null) return;

        var updated = await PlayerService.UpdateAsync(edited.Player);
        if (!Snackbar.ReportFailure(L, updated)) return;

        var name = edited.Player.DisplayName;
        if (edited.IsGuest == member.IsGuest)
        {
            Snackbar.Add(L["Player {0} updated", name], Severity.Success);
        }
        else
        {
            // The guest line replaces the generic one rather than joining it: two snackbars for one
            // Save reads as two things having happened.
            var guest = await SquadService.SetGuestAsync(seasonId, member.PlayerId, edited.IsGuest);
            Snackbar.Report(L, guest, edited.IsGuest
                ? L["{0} is now a guest", name]
                : L["{0} is now a squad player", name]);
        }

        await LoadAsync();
    }

    /// <summary>Retires someone who has left the club, or brings them back. Nothing is destroyed
    /// either way, so this is the action to reach for instead of Delete.</summary>
    private async Task ToggleArchived(Player player)
    {
        var name = player.DisplayName;

        // Only archiving asks. Restoring puts someone back into the pickers and costs nothing if it
        // was not meant — a confirm there is a click that teaches people to click through confirms.
        var confirmed = player.IsArchived || await DialogService.ConfirmAsync(
            L["Archive player"],
            L["Archive {0}? They keep every appearance, goal and season they played, and stop being offered for seasons to come.", name],
            "Archive");
        if (!confirmed) return;

        var result = await PlayerService.SetArchivedAsync(player.Id, !player.IsArchived);
        Snackbar.Report(L, result, player.IsArchived
            ? L["{0} is back in the squad lists", name]
            : L["{0} archived", name]);
        await LoadAsync();
    }

    /// <summary>
    /// Deletes the person outright. The service refuses once they have played, so the dialog says
    /// what is about to be lost rather than asking a bare "are you sure" about a cascade nobody can
    /// see: it is the confirmation for someone entered by mistake, and the refusal carries the rest.
    /// </summary>
    private async Task DeletePlayer(Player player)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Player"],
            L["Delete {0} from every season, along with any minutes and goals on record? Archive them instead to keep their history.", player.DisplayName]);
        if (!confirmed) return;

        var result = await PlayerService.DeleteAsync(player.Id);
        Snackbar.Report(L, result, L["Player {0} deleted", player.DisplayName], Severity.Warning);
        await LoadAsync();
    }

    /// <summary>Returns the edited player and guest flag, or null when the dialog was cancelled.</summary>
    private async Task<PlayerEdit?> ShowPlayerDialogAsync(
        string title, Player? player = null, bool isGuest = false)
    {
        return await DialogService.PromptAsync<PlayerDialog, PlayerEdit>(title, p =>
        {
            if (player is not null) p.Add(x => x.Player, player);
            p.Add(x => x.IsGuest, isGuest);
            p.Add(x => x.SeasonName, SeasonName);
        });
    }

    /// <summary>Returns the picked player and guest status, or null when cancelled.</summary>
    private async Task<SquadMemberChoice?> ShowSquadMemberDialogAsync(List<Player> candidates)
    {
        return await DialogService.PromptAsync<SquadMemberDialog, SquadMemberChoice>(
            L["Add to squad"], p => p.Add(x => x.Candidates, candidates));
    }
}
