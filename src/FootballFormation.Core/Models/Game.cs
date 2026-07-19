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
