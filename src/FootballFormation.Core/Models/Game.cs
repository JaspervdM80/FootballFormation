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

    /// <summary>What kind of fixture this is. Descriptive — it does not affect statistics.</summary>
    public MatchType MatchType { get; set; } = MatchType.Competition;

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
    public List<GameSubstitution> Substitutions { get; set; } = [];

    /// <summary>
    /// Admin-written notes about this game, most of them private. Deliberately not eager-loaded
    /// anywhere — every read goes through <c>GameService.GetCommentsAsync</c>, which is the one
    /// place the public/private split is applied.
    /// </summary>
    public List<GameComment> Comments { get; set; } = [];

    /// <summary>How far the live match screen has got with this game.</summary>
    public MatchState MatchState { get; set; } = MatchState.NotStarted;

    /// <summary>
    /// UTC instant the match clock was last started or resumed; null whenever the clock is not
    /// running. The clock is stored as an anchor rather than a ticking value so every viewer
    /// derives the same elapsed time without the server having to push each second.
    /// </summary>
    public DateTime? ClockRunningSince { get; set; }

    /// <summary>Seconds banked from earlier running stretches, excluding the current one.</summary>
    public int ClockAccumulatedSeconds { get; set; }

    /// <summary>The period currently on the pitch. Null before kick-off, at the break, and after the final whistle.</summary>
    public int? LivePeriodId { get; set; }

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
    /// True once the game's data is settled enough to count towards statistics: the final whistle
    /// was blown on the live screen, or the game was never run live but has a final score on file.
    /// A match in progress never counts, however many goals are already logged — otherwise the
    /// season table and scorer lists would shift while the game is still being played.
    /// </summary>
    public bool IsComplete => MatchState == MatchState.Finished
        || (MatchState == MatchState.NotStarted && ScoreHome.HasValue && ScoreAway.HasValue);

    /// <summary>
    /// True when at least one period was actually kicked off, i.e. the game has real timings and
    /// the planned lineup is no longer the best source for who played how long.
    /// </summary>
    public bool HasActualTimings => Periods.Any(p => p.StartedAtSeconds is not null);

    /// <summary>
    /// How long the match really lasted, summed over the periods that were played out. Falls back
    /// to the scheduled duration when the game was never run live. This is the denominator for a
    /// player's available minutes, so utilisation cannot exceed 100% on a match that over-ran.
    /// </summary>
    public int PlayedDurationMinutes => HasActualTimings
        ? Periods
            .Where(p => p.StartedAtSeconds is not null && p.EndedAtSeconds is not null)
            .Sum(p => p.EndedAtSeconds!.Value - p.StartedAtSeconds!.Value) / 60
        : GameDurationMinutes;

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

    public bool IsClockRunning => ClockRunningSince is not null;

    /// <summary>
    /// The period the match is currently about: the one being played; at a break and after the
    /// final whistle the last one that was; and before kick-off the first one. Shared by the live
    /// screen and the goal log so the minute written down is the one that was on screen.
    /// </summary>
    public GamePeriod? CurrentOrLastPeriod()
    {
        if (LivePeriodId is { } liveId
            && Periods.FirstOrDefault(p => p.Id == liveId) is { } live) return live;

        var lastPlayed = Periods
            .Where(p => p.StartedAtSeconds is not null)
            .OrderByDescending(p => p.StartedAtSeconds)
            .FirstOrDefault();

        return lastPlayed ?? Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
    }

    /// <summary>
    /// The match clock in seconds at <paramref name="utcNow"/>. Callers that only need a settled
    /// value (a paused clock, a finished match) can pass any instant.
    /// </summary>
    public int ElapsedSecondsAt(DateTime utcNow) => ClockAccumulatedSeconds +
        (ClockRunningSince is null ? 0 : Math.Max(0, (int)(utcNow - ClockRunningSince.Value).TotalSeconds));

    /// <summary>
    /// Our goals, counted from the logged goal rows. An own goal by one of ours counts for them,
    /// so it is excluded here and included in <see cref="CountTheirGoals"/>.
    /// </summary>
    public static int CountOurGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => !g.IsOwnGoal && !g.IsOpponentGoal);

    /// <summary>Their goals: everything the opponent scored, plus our own goals.</summary>
    public static int CountTheirGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => g.IsOwnGoal || g.IsOpponentGoal);
}

/// <summary>
/// Putting a set of games in date order.
/// <para>
/// SQLite has no date type — <see cref="Game.Date"/> lives in a TEXT column — so an
/// <c>ORDER BY</c> in the database compares the <em>text</em> a date was written as rather than
/// the date itself. The two only agree while every row was written in exactly the same format;
/// one row stored with a different separator or precision (a restored backup, a value written by
/// anything but this app) lands in the wrong place and nothing on screen looks wrong. Sorting
/// once the rows are materialised compares the parsed <see cref="DateTime"/>, which cannot drift.
/// </para>
/// <para>
/// Both spell the tie-break out, so two fixtures on the same day always come back in the order
/// they were entered instead of in whatever order the database happened to hand them over.
/// </para>
/// </summary>
public static class GameOrdering
{
    /// <summary>Newest first — the order the games list and the season reports read in.</summary>
    public static List<Game> NewestFirst(this IEnumerable<Game> games) =>
        [.. games.OrderByDescending(g => g.Date).ThenBy(g => g.Id)];

    /// <summary>Oldest first.</summary>
    public static List<Game> OldestFirst(this IEnumerable<Game> games) =>
        [.. games.OrderBy(g => g.Date).ThenBy(g => g.Id)];
}

public enum MatchState
{
    NotStarted,
    InProgress,
    Finished
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
