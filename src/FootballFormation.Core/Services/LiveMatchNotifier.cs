namespace FootballFormation.Core.Services;

/// A singleton and deliberately in-process, because the app runs as a single Fly.io instance. Scaling out would leave viewers on another
/// instance silently un-updated, and needs a real backplane rather than a patch.
public class LiveMatchNotifier
{
    /// Raised with the id of the game that changed. Subscribers must filter on it.
    public event Action<int>? Changed;

    public void Notify(int gameId) => Changed?.Invoke(gameId);
}
