using FootballFormation.UI.State;

namespace FootballFormation.UI.Components;

/// Waits for the season list before the page queries anything with it. There is nothing to subscribe to: choosing a season navigates to
/// /season/set, so the page that comes back is a fresh render that read the new choice off its own request.
public abstract class SeasonAwarePage : CancellableComponent
{
    [Inject] protected SeasonState SeasonState { get; set; } = null!;

    /// Null means "all seasons".
    protected int? SeasonId => SeasonState.SelectedSeasonId;

    protected override async Task OnInitializedAsync()
    {
        // No Cancellation on purpose: the memoized task belongs to the scope, not to this page, and the layout's picker shares it.
        await SeasonState.EnsureLoadedAsync();

        await OnInitializedCoreAsync();
        await LoadAsync();
    }

    /// Runs once, after the season list is available and before <see cref="LoadAsync"/>.
    protected virtual Task OnInitializedCoreAsync() => Task.CompletedTask;

    /// Loads what the page shows, for the currently selected season.
    protected abstract Task LoadAsync();
}
