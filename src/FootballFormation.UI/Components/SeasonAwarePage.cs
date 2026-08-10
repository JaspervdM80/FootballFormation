using FootballFormation.UI.State;
using Microsoft.AspNetCore.Components;

namespace FootballFormation.UI.Components;

/// <summary>
/// Base for a page whose contents depend on the season picked in the app bar.
/// <para>
/// It handles the part every such page had copied verbatim: wait for the season list, subscribe to
/// changes, re-run the page's load on the UI thread when the choice changes, and unsubscribe on
/// dispose. Forgetting that last step leaks the page for the life of the circuit, which is exactly
/// the kind of thing a base class should make impossible rather than ask each page to remember.
/// </para>
/// <para>
/// Override <see cref="LoadAsync"/> with whatever the page shows. It is called once on init and
/// again after every season change.
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
        // Cancellation: the task belongs to the circuit, not to this page.
        await SeasonState.EnsureLoadedAsync();
        SeasonState.OnChanged += OnSeasonChanged;

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

    private void OnSeasonChanged() => _ = InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    public override void Dispose()
    {
        SeasonState.OnChanged -= OnSeasonChanged;
        base.Dispose();
    }
}
