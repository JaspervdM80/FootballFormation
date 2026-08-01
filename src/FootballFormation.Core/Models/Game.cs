namespace FootballFormation.Core.Models;

public class Game
{
    public int Id { get; set; }
    public required string Opponent { get; set; }
    public DateTime Date { get; set; }

    /// <summary>The season this game counts towards. Derived from <see cref="Date"/> when the game
    /// is created (see <c>SeasonService.GetOrCreateForDateAsync</c>) but reassignable afterwards.</summary>
    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    public string? Notes { get; set; }
    public FormationType FormationType { get; set; }
    public GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    public int GameDurationMinutes { get; set; } = 60;

    /// <summary>True when we play at home, false for an away fixture.</summary>
    public bool IsHomeGame { get; set; } = true;

    /// <summary>Our score. Not tied to venue — see <see cref="IsHomeGame"/>.</summary>
    public int? ScoreHome { get; set; }

    /// <summary>The opponent's score. Not tied to venue — see <see cref="IsHomeGame"/>.</summary>
    public int? ScoreAway { get; set; }

    public List<GamePeriod> Periods { get; set; } = [];
    public List<GameGoal> Goals { get; set; } = [];

    /// <summary>Squad players opted out of this game.</summary>
    public List<int> UnavailablePlayerIds { get; set; } = [];

    /// <summary>Guests of this game's season, explicitly opted in to this game.</summary>
    public List<int> GuestPlayerIds { get; set; } = [];

    /// <summary>How many periods this game is split into.</summary>
    public int PeriodCount => SplitType.PeriodCount();

    /// <summary>Minutes each period lasts, assuming an even split of the game duration.</summary>
    public int PeriodDurationMinutes => PeriodCount == 0 ? 0 : GameDurationMinutes / PeriodCount;

    /// <summary>
    /// True when at least one period has a player placed on the pitch. Only meaningful when
    /// <see cref="Periods"/> are loaded with their <see cref="GamePeriod.PlayerPositions"/>
    /// (e.g. via <c>GetAllWithDetailsAsync</c>). Playing time is derived from lineups, so a
    /// game without one produces no minutes for anyone.
    /// </summary>
    public bool HasLineup => Periods.Any(p => p.PlayerPositions.Count > 0);

    /// <summary>
    /// Squad players are in unless marked unavailable; guests are out unless explicitly added.
    /// <para>
    /// Guest status is per season, so the season's squad has to be passed in. Anyone outside the
    /// squad is treated as a guest — three membership states collapse to the same two branches the
    /// rule always had.
    /// </para>
    /// </summary>
    public bool IsInRoster(Player player, SeasonSquad squad) => squad.IsFullMember(player.Id)
        ? !UnavailablePlayerIds.Contains(player.Id)
        : GuestPlayerIds.Contains(player.Id);

    /// <summary>
    /// Overload for reports that walk games across several seasons: the game picks its own season's
    /// squad, so a player who was a guest one year and a regular the next is judged correctly in each.
    /// </summary>
    public bool IsInRoster(Player player, SeasonSquads squads) => IsInRoster(player, squads.For(SeasonId));

    /// <summary>Everyone taking part in this game, from the full player pool.</summary>
    public List<Player> SelectRoster(IEnumerable<Player> allPlayers, SeasonSquad squad) =>
        [.. allPlayers.Where(p => IsInRoster(p, squad))];
}

public enum GameSplitType
{
    Halves,
    Quarters
}

public static class GameSplitTypeExtensions
{
    /// <summary>Derived from the period table itself, so the two can never drift apart.</summary>
    public static int PeriodCount(this GameSplitType splitType) =>
        PeriodTypeExtensions.ForSplitType(splitType).Length;

    /// <summary>Singular noun for one period, for use in sentences ("copy to next half").</summary>
    public static string PeriodLabel(this GameSplitType splitType) =>
        splitType == GameSplitType.Halves ? "half" : "quarter";
}
