namespace FootballFormation.Core.Models;

public class GameGoal
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    /// Null for an opponent goal — we do not track their players.
    public int? ScorerId { get; set; }
    public Player? Scorer { get; set; }

    public int? AssisterId { get; set; }
    public Player? Assister { get; set; }

    /// Null for a goal typed in on the result page, which has no half behind it. The id alone on purpose: every reader resolves it
    /// against the half already loaded on the game, so a navigation would only invite Include-ing the same row twice.
    public int? GamePeriodId { get; set; }

    /// Null for a goal typed in by hand. With <see cref="GamePeriodId"/> it is all the displayed minute is derived from, so correcting a
    /// half's timings corrects its goals too.
    public int? AtSeconds { get; set; }

    /// Typed in on the result page, and also what a goal logged before <see cref="AtSeconds"/> existed still shows: nothing left in such
    /// a row says whether a 37 in a 35-minute half was stoppage time or a hand-typed number. See docs/known_issues/live-match.md.
    public int? Minute { get; set; }

    /// One of ours put it in our own net. Counts for the opponent.
    public bool IsOwnGoal { get; set; }

    /// The opponent scored. Counts for the opponent, and has no scorer.
    public bool IsOpponentGoal { get; set; }

    /// When this was entered, not the match minute: a goal and the substitution that followed it share a minute, and only this can put
    /// them in the order they happened.
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// The one statement of which end of the scoreline a goal lands on — <see cref="Game.CountOurGoals"/> and ScoreProgressionReport
    /// both read it rather than restating the rule.
    public bool CountsForUs => !IsOwnGoal && !IsOpponentGoal;
}
