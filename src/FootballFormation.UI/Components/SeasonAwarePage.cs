using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

/// <summary>
/// Base for a page whose contents depend on the season picked in the app bar.
/// <para>
/// It handles the part every such page had copied verbatim: wait for the season list to load
/// before the page queries anything with it.
/// </para>
/// <para>
/// There is nothing to subscribe to. Choosing a season is a navigation to <c>/season/set</c>, so
/// the page that comes back is a fresh render that read the new choice off its own request — where
/// this used to re-run <see cref="LoadAsync"/> inside the circuit on a change event.
/// </para>
/// <para>
/// Override <see cref="LoadAsync"/> with whatever the page shows.
/// </para>
/// </summary>
public abstract class SeasonAwarePage : CancellableComponent
{
    [Inject] protected SeasonState SeasonState { get; set; } = null!;

    /// <summary>The season to filter by. Null means "all seasons".</summary>
    protected int? SeasonId => SeasonState.SelectedSeasonId;

    protected override async Task OnInitializedAsync()
    {
        // The season filter has to be resolved before the first query. Memoized in SeasonState,
        // so the layout's picker and this page share the one round trip — and why it gets no
        // Cancellation: the task belongs to the scope, not to this page.
        await SeasonState.EnsureLoadedAsync();

        await OnInitializedCoreAsync();
        await LoadAsync();
    }

    /// <summary>
    /// Anything the page needs before its first load — reading the auth state, say. Runs once,
    /// after the season list is available and before <see cref="LoadAsync"/>.
    /// </summary>
    protected virtual Task OnInitializedCoreAsync() => Task.CompletedTask;

    /// <summary>Loads what the page shows, for the currently selected season.</summary>
    protected abstract Task LoadAsync();
}
