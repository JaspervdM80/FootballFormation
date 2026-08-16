namespace FootballFormation.Core.Models;

public class GameGoal
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    /// <summary>Null for an opponent goal — we don't track their players.</summary>
    public int? ScorerId { get; set; }
    public Player? Scorer { get; set; }

    public int? AssisterId { get; set; }
    public Player? Assister { get; set; }

    /// <summary>
    /// The half that was being played when the ball went in, as the line-up playing it — the same
    /// fact <see cref="GameSubstitution.GamePeriodId"/> carries. Null for a goal typed in on the
    /// result page, which has no half behind it.
    /// <para>
    /// The id alone, with no navigation beside it: every reader resolves it against the half
    /// already loaded on the game (<c>MatchClockReport</c>), so a navigation would only be an
    /// invitation to <c>Include</c> the same row a second time.
    /// </para>
    /// </summary>
    public int? GamePeriodId { get; set; }

    /// <summary>
    /// Match-clock second the ball went in, from the same clock a substitution is stamped from.
    /// Null for a goal typed in by hand. With <see cref="GamePeriodId"/> it is everything the
    /// displayed minute is derived from, so correcting a half's timings corrects its goals too.
    /// </summary>
    public int? AtSeconds { get; set; }

    /// <summary>
    /// The minute somebody typed in on the result page, where there is no clock to read. Also what
    /// a goal logged before this row carried a clock still shows, unless it was scored in stoppage
    /// time — those said so on the row and were moved onto the clock by the migration that
    /// introduced it (see docs/known_issues.md, "a goal's minute is derived, not stored").
    /// The rest keep only this, because nothing left in one says whether a 37 in a 35-minute half
    /// was stoppage time or a number typed in by hand.
    /// </summary>
    public int? Minute { get; set; }

    /// <summary>One of ours put it in our own net. Counts for the opponent.</summary>
    public bool IsOwnGoal { get; set; }

    /// <summary>The opponent scored. Counts for the opponent, and has no scorer.</summary>
    public bool IsOpponentGoal { get; set; }

    /// <summary>
    /// When this was entered, as opposed to the match minute it belongs to. Several things can
    /// share a minute — a goal and the substitution that followed it — and the minute alone cannot
    /// put them in the order they happened. Goals typed in afterwards on the result page get the
    /// moment they were typed, which is the best available answer and never reorders a live match.
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Which end of the scoreline this goal lands on. The rule is stated once here because two
    /// places count goals: <see cref="Game.CountOurGoals"/> totals a finished match, and
    /// <c>ScoreProgressionReport</c> walks them one at a time for the live timeline.
    /// </summary>
    public bool CountsForUs => !IsOwnGoal && !IsOpponentGoal;
}
