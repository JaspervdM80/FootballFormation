using Microsoft.JSInterop;

namespace FootballFormation.UI.State;

/// <summary>
/// Reads and writes the season picker's choice as a cookie (js/season.js), so it survives a reload.
/// <para>
/// That reload is not hypothetical: the app is one Fly machine, and every deploy — which is every
/// merge to main — drops every open circuit. Before this, everyone watching a match came back to
/// whichever season the database calls current.
/// </para>
/// <para>
/// Eight hours is a match day, not a subscription. Long enough that a Saturday spent in last
/// season's numbers stays there through however many restarts, short enough that it has forgotten
/// by the next time anyone opens the app.
/// </para>
/// </summary>
public class SeasonPreference(IJSRuntime js)
{
    /// <summary>"All seasons" is a choice, and null is already how it is spelled — so it needs a
    /// value of its own in the cookie to be distinguishable from nothing stored at all.</summary>
    private const string AllSeasons = "all";

    private const int LifetimeSeconds = 8 * 60 * 60;

    /// <summary>The stored choice, or null when there is nothing to restore.</summary>
    public async Task<StoredSeason?> LoadAsync()
    {
        var raw = await InteropAsync(() => js.InvokeAsync<string?>("seasonCookie.get"));

        if (string.IsNullOrEmpty(raw)) return null;
        if (raw == AllSeasons) return new StoredSeason(null);

        // A hand-edited or stale cookie is not worth an error — treat it as nothing stored.
        return int.TryParse(raw, out var id) ? new StoredSeason(id) : null;
    }

    public Task SaveAsync(int? seasonId) =>
        InteropAsync(() => js.InvokeVoidAsync(
            "seasonCookie.set", seasonId?.ToString() ?? AllSeasons, LifetimeSeconds));

    /// <summary>
    /// There are two moments where there is no JavaScript to call: the prerender pass, which runs
    /// before a circuit exists, and a circuit already on its way out. Neither is a failure worth
    /// surfacing — the picker falls back to the database's current season, and the interactive
    /// pass that follows the prerender reads the cookie for real.
    /// </summary>
    private static async Task<T?> InteropAsync<T>(Func<ValueTask<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException
                                      or JSException or OperationCanceledException)
        {
            return default;
        }
    }

    private static Task InteropAsync(Func<ValueTask> call) =>
        InteropAsync<object?>(async () => { await call(); return null; });
}

/// <summary>A restored choice. <see cref="SeasonId"/> null means "all seasons".</summary>
/// <param name="SeasonId">The season to filter by, or null for no filter.</param>
public sealed record StoredSeason(int? SeasonId);
