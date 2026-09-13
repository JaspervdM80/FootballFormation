namespace FootballFormation.Core.Services;

/// What a touchline write was. Only the three a spectator would want woken for are named; everything else the live screen still redraws
/// for is <see cref="Other"/>, which is the default so a call site that says nothing can never wake a phone by accident.
public enum LiveMatchEvent
{
    Other,
    KickOff,
    Goal,
    FullTime
}

/// A singleton and deliberately in-process, because the app runs as a single Fly.io instance. Scaling out would leave viewers on another
/// instance silently un-updated, and needs a real backplane rather than a patch.
public class LiveMatchNotifier
{
    /// Raised with the id of the game that changed and what happened to it. Subscribers must filter on both.
    public event Action<int, LiveMatchEvent>? Changed;

    public void Notify(int gameId, LiveMatchEvent change = LiveMatchEvent.Other) => Changed?.Invoke(gameId, change);
}
