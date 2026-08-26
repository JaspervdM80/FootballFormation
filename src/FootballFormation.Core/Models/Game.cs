namespace FootballFormation.Core.Models;

public class Game
{
    public int Id { get; set; }
    public required string Opponent { get; set; }
    public DateTime Date { get; set; }

    /// Derived from <see cref="Date"/> at creation (SeasonService.GetOrCreateForDateAsync), but reassignable afterwards.
    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    /// Descriptive only — it does not affect statistics.
    public MatchType MatchType { get; set; } = MatchType.Competition;

    public FormationType FormationType { get; set; }
    public GameSplitType SplitType { get; set; } = GameSplitType.Halves;
    public int GameDurationMinutes { get; set; } = 60;

    public bool IsHomeGame { get; set; } = true;

    /// Home/Away name the sides, not the venue: ScoreHome is always ours. <see cref="InVenueOrder"/> does the flip for display.
    public int? ScoreHome { get; set; }

    public int? ScoreAway { get; set; }

    /// <see cref="Date"/> carries both parts, so midnight is how "no kick-off time entered" is stored.
    public bool HasStartTime => Date.TimeOfDay != TimeSpan.Zero;

    public string DateLine(string format) =>
        HasStartTime ? $"{Date.ToString(format)}, {Date:HH:mm}" : Date.ToString(format);

    /// A live match writes the score as the goals go in, so a score alone only means the game started — the state has to be checked too.
    public bool HasFinalScore =>
        MatchState != MatchState.InProgress && ScoreHome.HasValue && ScoreAway.HasValue;

    /// A period row is a planned line-up, not a stretch of clock: a match runs in two halves whatever the split says, and quarters just
    /// plans two rows per half. The row opening a half is the one played; a later row in the same half is a change the coach makes by hand.
    public List<GamePeriod> Periods { get; set; } = [];
    public List<GameGoal> Goals { get; set; } = [];
    public List<GameSubstitution> Substitutions { get; set; } = [];

    public List<GameInjury> Injuries { get; set; } = [];

    /// Never eager-load these: GameService.GetCommentsAsync is the one place the public/private split is applied.
    public List<GameComment> Comments { get; set; } = [];

    public MatchState MatchState { get; set; } = MatchState.NotStarted;

    /// Null whenever the clock is not running. An anchor rather than a ticking value, so every viewer derives the same elapsed time
    /// without the server pushing each second.
    public DateTime? ClockRunningSince { get; set; }

    /// Banked from earlier running stretches only — the current one is not in here. <see cref="ElapsedSecondsAt"/> adds it.
    public int ClockAccumulatedSeconds { get; set; }

    /// Null before kick-off, at half time and after the final whistle — exactly the moments nothing may be recorded against a half.
    public int? LivePeriodId { get; set; }

    public List<int> UnavailablePlayerIds { get; set; } = [];

    /// May name a player <see cref="UnavailablePlayerIds"/> already names; injury is the more specific answer. Written by
    /// StandingInjuries.RecordAsync, which says why and when.
    public List<int> InjuredPlayerIds { get; set; } = [];

    /// An empty <see cref="InjuredPlayerIds"/> is otherwise indistinguishable from an unwritten one, and a September match with nobody
    /// hurt would be restamped with November's casualties the first time somebody retyped its scoreline.
    public bool AbsencesRecorded { get; set; }

    public List<int> GuestPlayerIds { get; set; } = [];

    public int PeriodCount => SplitType.PeriodCount();

    public int PeriodDurationSeconds => SplitType.PeriodDurationSeconds(GameDurationMinutes);

    /// For display only — the arithmetic uses <see cref="PeriodDurationSeconds"/>, which stays whole.
    public decimal PeriodDurationMinutes => SplitType.PeriodDurationMinutes(GameDurationMinutes);

    /// Silently false unless <see cref="Periods"/> were loaded with their PlayerPositions — GetAllWithDetailsAsync does, a plain load does not.
    public bool HasLineup => Periods.Any(p => p.PlayerPositions.Count > 0);

    /// A match in progress never counts, however many goals are logged, or the season table would shift while the game is being played.
    public bool IsComplete => MatchState == MatchState.Finished
        || (MatchState == MatchState.NotStarted && ScoreHome.HasValue && ScoreAway.HasValue);

    /// Once true, the planned line-up is no longer the best source for who played how long.
    public bool HasActualTimings => Periods.Any(p => p.StartedAtSeconds is not null);

    /// Zero on a game never run live, and a period still in progress contributes nothing — ask <see cref="HasActualTimings"/> first.
    public int PlayedDurationSeconds => Periods
        .Where(p => p.StartedAtSeconds is not null && p.EndedAtSeconds is not null)
        .Sum(p => p.EndedAtSeconds!.Value - p.StartedAtSeconds!.Value);

    /// Rounding does not distribute over addition, so an accumulator spanning several games must stay in seconds and convert once at the
    /// very end — summing per-game minutes instead reads over 100% utilisation. See docs/known_issues/domain.md.
    public static int SecondsToMinutes(int seconds) => (int)Math.Round(seconds / 60.0);

    /// The multi-game form: sum this, not <see cref="PlayedDurationMinutes"/>, for the reason on <see cref="SecondsToMinutes"/>.
    public int PlayedDurationSecondsEffective => HasActualTimings
        ? PlayedDurationSeconds
        : GameDurationMinutes * 60;

    /// One game's utilisation denominator, falling back to the scheduled duration. Single-game only — see <see cref="SecondsToMinutes"/>.
    public int PlayedDurationMinutes => SecondsToMinutes(PlayedDurationSecondsEffective);

    /// The whole played duration, or the stretch up to the moment she was hurt. The multi-game form — see <see cref="SecondsToMinutes"/>.
    public int AvailableSecondsFor(int playerId)
    {
        // Only the live screen writes an injury, so a game never run live can have none.
        if (!HasActualTimings) return PlayedDurationSecondsEffective;

        return Injuries.FirstOrDefault(i => i.PlayerId == playerId) is { } injury
            ? Math.Min(injury.AtSeconds, PlayedDurationSeconds)
            : PlayedDurationSecondsEffective;
    }

    /// An injury at 20' leaves her judged on 20 minutes, not on the hour she was never going to get. Single-game only — see
    /// <see cref="SecondsToMinutes"/>.
    public int AvailableMinutesFor(int playerId) => SecondsToMinutes(AvailableSecondsFor(playerId));

    /// Matched on the second as well as the player: one action wrote both. Pairing on the player alone would read an earlier
    /// substitution in the same half as this injury's replacement.
    public bool WasReplaced(GameInjury injury) => Substitutions
        .Any(s => s.GamePeriodId == injury.GamePeriodId
                  && s.PlayerOffId == injury.PlayerId
                  && s.AtSeconds == injury.AtSeconds);

    /// The other side of <see cref="WasReplaced"/>.
    public GameInjury? InjuryFor(GameSubstitution substitution) => Injuries
        .FirstOrDefault(i => i.GamePeriodId == substitution.GamePeriodId
                             && i.PlayerId == substitution.PlayerOffId
                             && i.AtSeconds == substitution.AtSeconds);

    /// Squad players are in unless marked unavailable; guests are out unless explicitly added. Blind to live status on purpose — this
    /// judges a game as it was played, and injuring someone today would otherwise zero out every game she already played.
    public bool IsInRoster(Player player, SeasonSquad squad) => squad.IsFullMember(player.Id)
        ? !UnavailablePlayerIds.Contains(player.Id) && !InjuredPlayerIds.Contains(player.Id)
        : GuestPlayerIds.Contains(player.Id);

    /// For reports walking several seasons: each game picks its own season's squad, so a guest one year and a regular the next is judged
    /// correctly in each.
    public bool IsInRoster(Player player, SeasonSquads squads) => IsInRoster(player, squads.For(SeasonId));

    public List<Player> SelectRoster(IEnumerable<Player> allPlayers, SeasonSquad squad) =>
        [.. allPlayers.Where(p => IsInRoster(p, squad))];

    public bool IsClockRunning => ClockRunningSince is not null;

    /// Never null while the game has periods: the one on the pitch, else the last played, else the one it opens with.
    public GamePeriod? CurrentOrLastHalf()
    {
        if (LiveHalf() is { } live) return live;

        var lastPlayed = Periods
            .Where(p => p.StartedAtSeconds is not null)
            .OrderByDescending(p => p.StartedAtSeconds)
            .FirstOrDefault();

        return lastPlayed ?? Periods.OrderBy(p => p.PeriodType).FirstOrDefault();
    }

    /// Stricter than <see cref="CurrentOrLastHalf"/>: null unless a half is actually being played, so this is the one a sub may touch.
    public GamePeriod? LiveHalf() =>
        LivePeriodId is null ? null : Periods.FirstOrDefault(p => p.Id == LivePeriodId);

    /// Null once both halves have run. A quarters game plans two rows per half but plays two halves, so after the first half this is the
    /// third quarter's line-up, not the second quarter's plan left behind inside the half just played.
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

    /// The clock never stops for this one — it is a plan the coach works through by hand, not a step the match takes.
    public GamePeriod? MidHalfPlan(GamePeriod half) =>
        Periods
            .OrderBy(p => p.PeriodType)
            .FirstOrDefault(p => p.PeriodType > half.PeriodType
                                 && p.PeriodType.Half() == half.PeriodType.Half());

    /// <paramref name="utcNow"/> is ignored while the clock is stopped, so a settled value can be read with any instant.
    public int ElapsedSecondsAt(DateTime utcNow) => ClockAccumulatedSeconds +
        (ClockRunningSince is null ? 0 : Math.Max(0, (int)(utcNow - ClockRunningSince.Value).TotalSeconds));

    /// An own goal by one of ours counts for them, so it lands in <see cref="CountTheirGoals"/> instead.
    public static int CountOurGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => g.CountsForUs);

    /// Everything the opponent scored, plus our own goals.
    public static int CountTheirGoals(IEnumerable<GameGoal> goals) =>
        goals.Count(g => !g.CountsForUs);

    /// Recounts rather than increments, which is what makes a live scoreline self-correcting.
    public void CountScoreFrom(IReadOnlyCollection<GameGoal> goals)
    {
        ScoreHome = CountOurGoals(goals);
        ScoreAway = CountTheirGoals(goals);
    }

    /// The one flip between what is stored (always us/them) and how a scoreline is shown. Everything that displays one goes through here.
    public VenueScore InVenueOrder(int us, int them) => IsHomeGame ? new VenueScore(us, them) : new VenueScore(them, us);

    /// A null score reads as 0-0.
    public VenueScore ScoreboardOrder() => InVenueOrder(ScoreHome ?? 0, ScoreAway ?? 0);
}

/// In memory, never in SQL — see QueryTags.ComparesDatesInSql. The tie-break is spelled out so same-day fixtures keep entry order.
public static class GameOrdering
{
    public static List<Game> NewestFirst(this IEnumerable<Game> games) =>
        [.. games.OrderByDescending(g => g.Date).ThenBy(g => g.Id)];

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
    /// Derived from the period table itself, so the two can never drift apart.
    public static int PeriodCount(this GameSplitType splitType) =>
        PeriodTypeExtensions.ForSplitType(splitType).Length;

    /// Seconds rather than minutes because 60 divides by every period count there is, so a 50-minute game in quarters still adds back up
    /// to 50 instead of losing the remainder to integer division.
    public static int PeriodDurationSeconds(this GameSplitType splitType, int gameDurationMinutes)
    {
        var count = splitType.PeriodCount();
        return count == 0 ? 0 : gameDurationMinutes * 60 / count;
    }

    /// For display only — fractional when the split does not land on a whole minute.
    public static decimal PeriodDurationMinutes(this GameSplitType splitType, int gameDurationMinutes) =>
        splitType.PeriodDurationSeconds(gameDurationMinutes) / 60m;

    public static string PeriodLabel(this GameSplitType splitType) =>
        splitType == GameSplitType.Halves ? "half" : "quarter";
}
