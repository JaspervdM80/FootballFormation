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
    /// The minute on the scoreboard clock, which never runs past the end of the half — the overrun
    /// is <see cref="AdditionalMinute"/>. Null on a goal recorded with no minute at all, which the
    /// result page allows.
    /// </summary>
    public int? Minute { get; set; }

    /// <summary>
    /// Minutes into stoppage time, counted from 1, or zero for a goal in normal play. Stored beside
    /// the minute rather than added into it because the two together are what orders the timeline:
    /// a goal at 35+2 belongs before one at 36, and a single number that had counted on to 37 would
    /// put it after. Always zero on a goal typed in by hand, which has no clock behind it.
    /// </summary>
    public int AdditionalMinute { get; set; }

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
