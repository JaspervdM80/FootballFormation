namespace FootballFormation.Core.Models;

/// <summary>
/// One season's squad, as an immutable lookup.
/// <para>
/// Passed to <see cref="Game.IsInRoster(Player, SeasonSquad)"/> so the roster rule stays on the
/// model (see docs/patterns/service-structure.md) while the per-season data it now needs arrives as an explicit
/// argument rather than a navigation property that may or may not have been loaded.
/// </para>
/// </summary>
public sealed class SeasonSquad
{
    public static readonly SeasonSquad Empty = new(0, []);

    private readonly Dictionary<int, SeasonSquadMember> _byPlayerId;

    public SeasonSquad(int seasonId, IEnumerable<SeasonSquadMember> members)
    {
        SeasonId = seasonId;

        // Guests last, then shirt number, then name — the ordering PlayerService.GetAllAsync
        // used to own via OrderBy(p => p.IsGuest), back when guest status was global.
        Members =
        [
            .. members
                .OrderBy(m => m.IsGuest)
                .ThenBy(m => m.Player?.ShirtNumber ?? int.MaxValue)
                .ThenBy(m => m.Player?.FirstName)
                .ThenBy(m => m.Player?.Surname)
        ];

        _byPlayerId = Members.ToDictionary(m => m.PlayerId);
    }

    public int SeasonId { get; }

    public IReadOnlyList<SeasonSquadMember> Members { get; }

    public bool Contains(int playerId) => _byPlayerId.ContainsKey(playerId);

    /// <summary>
    /// True for guests <em>and</em> for anyone outside the squad. Both mean "not a regular", which
    /// is exactly the distinction the roster rule needs, and it keeps games that reference a
    /// since-departed player rendering sensibly.
    /// </summary>
    public bool IsGuest(int playerId) =>
        !_byPlayerId.TryGetValue(playerId, out var member) || member.IsGuest;

    public bool IsFullMember(int playerId) =>
        _byPlayerId.TryGetValue(playerId, out var member) && !member.IsGuest;

    /// <summary>False for anyone outside the squad — an injury is a status a membership row carries,
    /// so someone who was never a member was never marked injured either.</summary>
    public bool IsInjured(int playerId) =>
        _byPlayerId.TryGetValue(playerId, out var member) && member.IsInjured;

    /// <summary>Everyone in the squad, guests last. Members must have been loaded with their Player.</summary>
    public List<Player> Players => [.. Members.Where(m => m.Player is not null).Select(m => m.Player!)];

    public List<Player> FullMembers =>
        [.. Members.Where(m => !m.IsGuest && m.Player is not null).Select(m => m.Player!)];

    public List<Player> Guests =>
        [.. Members.Where(m => m.IsGuest && m.Player is not null).Select(m => m.Player!)];

    public List<Player> Injured =>
        [.. Members.Where(m => m.IsInjured && m.Player is not null).Select(m => m.Player!)];
}

/// <summary>
/// Squads for several seasons at once, for reports that span them (the picker's "All seasons").
/// Each <see cref="Game"/> knows its own <see cref="Game.SeasonId"/>, so it can pick the right
/// squad out of this set itself — callers never have to.
/// </summary>
public sealed class SeasonSquads
{
    public static readonly SeasonSquads Empty = new([]);

    private readonly Dictionary<int, SeasonSquad> _bySeasonId;

    public SeasonSquads(IEnumerable<SeasonSquadMember> members) =>
        _bySeasonId = members
            .GroupBy(m => m.SeasonId)
            .ToDictionary(g => g.Key, g => new SeasonSquad(g.Key, g));

    public static SeasonSquads Of(SeasonSquad squad) => new(squad.Members);

    /// <summary>The squad for a season, or an empty one when that season has none yet.</summary>
    public SeasonSquad For(int seasonId) =>
        _bySeasonId.TryGetValue(seasonId, out var squad) ? squad : SeasonSquad.Empty;

    public List<Player> AllPlayers =>
        [.. _bySeasonId.Values.SelectMany(s => s.Players).DistinctBy(p => p.Id)];

    /// <summary>
    /// True when the player is a full squad member in at least one loaded season — the
    /// "belongs in the fairness table" test, which has to tolerate no single season being selected.
    /// </summary>
    public bool IsFullMemberAnywhere(int playerId) =>
        _bySeasonId.Values.Any(s => s.IsFullMember(playerId));
}
