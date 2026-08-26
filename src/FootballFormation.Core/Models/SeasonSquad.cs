namespace FootballFormation.Core.Models;

/// An explicit argument to <see cref="Game.IsInRoster(Player, SeasonSquad)"/> rather than a navigation property that may or may not have
/// been loaded, which is what keeps the roster rule on the model. See docs/patterns/service-structure.md.
public sealed class SeasonSquad
{
    public static readonly SeasonSquad Empty = new(0, []);

    private readonly Dictionary<int, SeasonSquadMember> _byPlayerId;

    public SeasonSquad(int seasonId, IEnumerable<SeasonSquadMember> members)
    {
        SeasonId = seasonId;

        // Guests last, then shirt number, then name — the one ordering every squad list is read in.
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

    /// True for anyone outside the squad as well as for guests: both mean "not a regular", which keeps a game referencing a
    /// since-departed player rendering sensibly.
    public bool IsGuest(int playerId) =>
        !_byPlayerId.TryGetValue(playerId, out var member) || member.IsGuest;

    public bool IsFullMember(int playerId) =>
        _byPlayerId.TryGetValue(playerId, out var member) && !member.IsGuest;

    /// False for anyone outside the squad — injury is a status the membership row carries, so a non-member was never marked injured.
    public bool IsInjured(int playerId) =>
        _byPlayerId.TryGetValue(playerId, out var member) && member.IsInjured;

    /// Silently empty unless Members were loaded with their Player.
    public List<Player> Players => [.. Members.Where(m => m.Player is not null).Select(m => m.Player!)];

    public List<Player> FullMembers =>
        [.. Members.Where(m => !m.IsGuest && m.Player is not null).Select(m => m.Player!)];

    public List<Player> Guests =>
        [.. Members.Where(m => m.IsGuest && m.Player is not null).Select(m => m.Player!)];

    public List<Player> Injured =>
        [.. Members.Where(m => m.IsInjured && m.Player is not null).Select(m => m.Player!)];
}

/// For reports spanning seasons. Each <see cref="Game"/> knows its own SeasonId, so it picks the right squad out of this itself.
public sealed class SeasonSquads
{
    public static readonly SeasonSquads Empty = new([]);

    private readonly Dictionary<int, SeasonSquad> _bySeasonId;

    public SeasonSquads(IEnumerable<SeasonSquadMember> members) =>
        _bySeasonId = members
            .GroupBy(m => m.SeasonId)
            .ToDictionary(g => g.Key, g => new SeasonSquad(g.Key, g));

    public static SeasonSquads Of(SeasonSquad squad) => new(squad.Members);

    /// An empty squad when that season has none yet, never null.
    public SeasonSquad For(int seasonId) =>
        _bySeasonId.TryGetValue(seasonId, out var squad) ? squad : SeasonSquad.Empty;

    public List<Player> AllPlayers =>
        [.. _bySeasonId.Values.SelectMany(s => s.Players).DistinctBy(p => p.Id)];

    /// The "belongs in the fairness table" test, which has to tolerate no single season being selected.
    public bool IsFullMemberAnywhere(int playerId) =>
        _bySeasonId.Values.Any(s => s.IsFullMember(playerId));
}
