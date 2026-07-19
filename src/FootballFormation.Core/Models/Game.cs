namespace FootballFormation.Core.Models;

public class Game
{
    public int Id { get; set; }
    public required string Opponent { get; set; }
    public DateTime Date { get; set; }
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

    /// <summary>Guest players explicitly opted in to this game.</summary>
    public List<int> GuestPlayerIds { get; set; } = [];

    /// <summary>How many periods this game is split into.</summary>
    public int PeriodCount => SplitType.PeriodCount();

    /// <summary>Minutes each period lasts, assuming an even split of the game duration.</summary>
    public int PeriodDurationMinutes => PeriodCount == 0 ? 0 : GameDurationMinutes / PeriodCount;

    /// <summary>
    /// Squad players are in unless marked unavailable; guests are out unless explicitly added.
    /// </summary>
    public bool IsInRoster(Player player) => player.IsGuest
        ? GuestPlayerIds.Contains(player.Id)
        : !UnavailablePlayerIds.Contains(player.Id);

    /// <summary>Everyone taking part in this game, from the full player pool.</summary>
    public List<Player> SelectRoster(IEnumerable<Player> allPlayers) =>
        allPlayers.Where(IsInRoster).ToList();
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
