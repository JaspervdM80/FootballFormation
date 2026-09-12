using FootballFormation.Core.Services;

namespace FootballFormation.Core.Reporting;

/// A null <paramref name="ScorerName"/> on a <see cref="LiveMatchEvent.Goal"/> means it was not ours to celebrate — an opponent goal or
/// an own goal, both of which still move the scoreline a follower is watching.
public record MatchNotification(
    LiveMatchEvent Event,
    string Opponent,
    bool IsHomeGame,
    VenueScore Score,
    string? ScorerName,
    MatchMinute? Minute);

/// Pure data, no localized text, as MatchSummaryReport is: the words belong to MatchNotificationTextBuilder.
///
/// Nothing here may carry playing minutes. A notification is read on a lock screen by whoever is holding the phone, which makes it the
/// least recoverable place in the app to leak what the stats pages keep behind an admin sign-in.
public static class MatchNotificationReport
{
    public static MatchNotification Build(Game game, LiveMatchEvent change)
    {
        var goal = change == LiveMatchEvent.Goal ? LatestGoal(game) : null;

        return new MatchNotification(
            change,
            game.Opponent,
            game.IsHomeGame,
            game.ScoreboardOrder(),
            goal?.CountsForUs == true ? goal.Scorer?.DisplayName : null,
            goal is null ? null : MatchClockReport.MinuteOf(game, goal));
    }

    /// By when it was recorded rather than when it happened: the goal that triggered this is the one just typed in, even if the coach is
    /// catching up on one from five minutes ago.
    private static GameGoal? LatestGoal(Game game) => game.Goals
        .OrderByDescending(g => g.RecordedAt)
        .ThenByDescending(g => g.Id)
        .FirstOrDefault();
}
