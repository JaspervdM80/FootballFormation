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

    /// <summary>
    /// The line-ups this game is planned in. A match is played in two halves whatever the split
    /// says; a period row is a <em>planned line-up</em> for a stretch of one, and a quarters game
    /// simply plans two per half. The row that opens a half is the one the live match plays it
    /// with, and the row after it inside the same half is a plan the coach carries out by hand.
    /// </summary>
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
    /// UTC instant the match clock was last started — at kick-off, or when a period took over from
    /// a break; null whenever the clock is not running. The clock is stored as an anchor rather than a ticking value so every viewer
    /// derives the same elapsed time without the server having to push each second.
    /// </summary>
    public DateTime? ClockRunningSince { get; set; }

    /// <summary>Seconds banked from earlier running stretches, excluding the current one.</summary>
    public int ClockAccumulatedSeconds { get; set; }

    /// <summary>
    /// The line-up currently on the pitch — the row that opened the half being played. Null before
    /// kick-off, at half time and after the final whistle, which are exactly the moments nothing
    /// may be recorded against a half.
    /// </summary>
    public int? LivePeriodId { get; set; }

    /// <summary>Squad players opted out of this game.</summary>
    public List<int> UnavailablePlayerIds { get; set; } = [];

    /// <summary>Guests of this game's season, explicitly opted in to this game.</summary>
    public List<int> GuestPlayerIds { get; set; } = [];

    /// <summary>How many periods this game is split into.</summary>
    public int PeriodCount => SplitType.PeriodCount();

    /// <summary>Seconds each period lasts on an even split of the game duration.</summary>
    public int PeriodDurationSeconds => SplitType.PeriodDurationSeconds(GameDurationMinutes);

    /// <summary>
    /// Minutes each period lasts, fractional when the split does not land on a whole minute
    /// (50 in quarters is 4 × 12.5). For display — the arithmetic uses
    /// <see cref="PeriodDurationSeconds"/>.
    /// </summary>
    public decimal PeriodDurationMinutes => SplitType.PeriodDurationMinutes(GameDurationMinutes);

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
    /// The seconds the match really ran, summed over the periods that were played out. A period
    /// still in progress contributes nothing — it has no final whistle to measure against — which
    /// matches what <c>GameMinutesReport</c> credits when no clock reading is passed to it.
    /// Zero on a game that was never run live; ask <see cref="HasActualTimings"/> first.
    /// </summary>
    public int PlayedDurationSeconds => Periods
        .Where(p => p.StartedAtSeconds is not null && p.EndedAtSeconds is not null)
        .Sum(p => p.EndedAtSeconds!.Value - p.StartedAtSeconds!.Value);

    /// <summary>
    /// How long the match really lasted, summed over the periods that were played out. Falls back
    /// to the scheduled duration when the game was never run live. This is the denominator for a
    /// player's available minutes, so utilisation cannot exceed 100% on a match that over-ran.
    /// </summary>
    public int PlayedDurationMinutes => HasActualTimings
        ? PlayedDurationSeconds / 60
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
    /// The half the match is currently about, as the line-up it is played with: the one on the
    /// pitch; at half time and after the final whistle the last one played; and before kick-off the
    /// half the match opens with. Shared by the live screen and the goal log so the minute written
    /// down is the one that was on screen.
    /// </summary>
    public GamePeriod? CurrentOrLastHalf()
    {
        if (LiveHalf() is { } live) return live;

        var lastPlayed = Periods
            .Where(p => p.StartedAtSeconds is not null)
            .OrderByDescending(p => p.StartedAtSeconds)
            .FirstOrDefault();

        return lastPlayed ?? Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
    }

    /// <summary>
    /// The half being played, as the line-up on the pitch, or null before kick-off, at half time
    /// and after the final whistle. Stricter than <see cref="CurrentOrLastHalf"/>, which always
    /// names a half if there is one to name: this is the one a substitution may touch.
    /// </summary>
    public GamePeriod? LiveHalf() =>
        LivePeriodId is null ? null : Periods.FirstOrDefault(p => p.Id == LivePeriodId);

    /// <summary>
    /// The half the clock goes to next, as the line-up it opens with — the first line-up not yet
    /// kicked off whose half has not been played. Null once both halves have run.
    /// <para>
    /// A quarters game is planned as two line-ups per half but played as two halves, so once the
    /// first half has run the next half to kick off opens with the third quarter's line-up, not
    /// with the second quarter's plan left behind inside the half just played.
    /// </para>
    /// </summary>
    public GamePeriod? NextHalf()
    {
        var halvesPlayed = Periods
            .Where(p => p.StartedAtSeconds is not null)
            .Select(p => p.PeriodType.Half())
            .ToHashSet();

        return Periods
            .OrderBy(p => p.PeriodType)
            .FirstOrDefault(p => p.StartedAtSeconds is null && !halvesPlayed.Contains(p.PeriodType.Half()));
    }

    /// <summary>
    /// The line-up planned to take over partway through <paramref name="half"/>, or null when the
    /// half is played out with the one it kicked off with. The clock never stops for it: it is a
    /// plan the coach works through by hand, which is what makes it a reference rather than a step.
    /// </summary>
    public GamePeriod? MidHalfPlan(GamePeriod half) =>
        Periods
            .OrderBy(p => p.PeriodType)
            .FirstOrDefault(p => p.PeriodType > half.PeriodType
                                 && p.PeriodType.Half() == half.PeriodType.Half());

    /// <summary>
    /// The match clock in seconds at <paramref name="utcNow"/>. Callers that only need a settled
    /// value (a stopped clock, a finished match) can pass any instant.
    /// </summary>
    public int ElapsedSecondsAt(DateTime utcNow) => ClockAccumulatedSeconds +
        (ClockRunningSince is null ? 0 : Math.Max(0, (int)(utcNow - ClockRunningSince.Value).TotalSeconds));

    /// <summary>
    /// Our goals, counted from the logged goal rows. An own goal by one of ours counts for them,
    /// so it is excluded here and included in <see cref="CountTheirGoals"/>.
    /// </summary>
    public static int CountOurGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => g.CountsForUs);

    /// <summary>Their goals: everything the opponent scored, plus our own goals.</summary>
    public static int CountTheirGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => !g.CountsForUs);
}

/// <summary>
/// Putting a set of games in date order.
/// <para>
/// SQLite has no date type — <see cref="Game.Date"/> lives in a TEXT column — so an
/// <c>ORDER BY</c> in the database compares the <em>text</em> a date was written as rather than
/// the date itself. The two only agree while every row was written in exactly the same format;
/// one stored with a different separator or precision (a restored backup, a value written by
/// anything but this app) lands in the wrong place and nothing on screen looks wrong. Sorting
/// once the rows are materialised compares the parsed <see cref="DateTime"/>, which cannot drift.
/// </para>
/// <para>
/// Both spell the tie-break out, so two fixtures on the same day keep the order they were
/// entered in rather than whatever order the database handed them over in.
/// </para>
/// </summary>
public static class GameOrdering
{
    /// <summary>Newest first — how the games list and the season reports read.</summary>
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

    /// <summary>
    /// How long one period lasts, in seconds. Seconds rather than minutes because a duration that
    /// splits into fractions of a minute (50 in quarters, 45 in halves) still splits exactly into
    /// seconds — 60 divides by every period count there is — so the periods always add back up to
    /// the full match length instead of quietly losing the remainder to integer division.
    /// </summary>
    public static int PeriodDurationSeconds(this GameSplitType splitType, int gameDurationMinutes)
    {
        var count = splitType.PeriodCount();
        return count == 0 ? 0 : gameDurationMinutes * 60 / count;
    }

    /// <summary>The same length in minutes, fractional when it has to be. For display only.</summary>
    public static decimal PeriodDurationMinutes(this GameSplitType splitType, int gameDurationMinutes) =>
        splitType.PeriodDurationSeconds(gameDurationMinutes) / 60m;

    /// <summary>Singular noun for one period, for use in sentences ("copy to next half").</summary>
    public static string PeriodLabel(this GameSplitType splitType) =>
        splitType == GameSplitType.Halves ? "half" : "quarter";
}
