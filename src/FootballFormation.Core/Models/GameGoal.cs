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
