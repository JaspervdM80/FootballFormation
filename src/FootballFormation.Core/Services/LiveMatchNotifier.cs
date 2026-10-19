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

/// The team rides along because one deployment serves many: a subscriber with no game of its own yet filters on the team instead.
public readonly record struct LiveMatchChange(int TeamId, int GameId, LiveMatchEvent Event);

/// A singleton and deliberately in-process, because the app runs as a single Fly.io instance. Scaling out would leave viewers on another
/// instance silently un-updated, and needs a real backplane rather than a patch.
public class LiveMatchNotifier
{
    /// Raised for every team's matches, so subscribers must filter on the game or the team.
    public event Action<LiveMatchChange>? Changed;

    public void Notify(LiveMatchChange change) => Changed?.Invoke(change);
}
