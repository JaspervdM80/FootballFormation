namespace FootballFormation.Core.Services;

/// <summary>
/// Tells every open live match screen that a game changed, so spectators see a goal or a
/// substitution without refreshing.
/// <para>
/// Registered as a singleton and deliberately in-process: the app runs as a single Fly.io
/// instance. If it is ever scaled out, viewers on another instance would stop updating and this
/// needs replacing with a backplane (Redis, SignalR backplane) rather than patching around.
/// </para>
/// </summary>
public class LiveMatchNotifier
{
    /// <summary>Raised with the id of the game that changed. Subscribers must filter on it.</summary>
    public event Action<int>? Changed;

    public void Notify(int gameId) => Changed?.Invoke(gameId);
}
