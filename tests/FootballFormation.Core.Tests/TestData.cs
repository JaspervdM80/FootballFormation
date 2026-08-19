using FootballFormation.Core.Models;

namespace FootballFormation.Core.Tests;

/// <summary>
/// Builders for the object graphs the domain and report tests need. Games in this app are a
/// four-level graph (game → periods → lineups → players) and constructing one inline buries the
/// single fact a test is actually about, so the shape is built here once.
/// </summary>
internal static class TestData
{
    public static Player Player(int id, string name = "Player", PlayerPosition preferred = PlayerPosition.CM,
        int? shirt = null, params PlayerPosition[] alternatives) =>
        new()
        {
            Id = id,
            FirstName = name,
            ShirtNumber = shirt,
            PreferredPosition = preferred,
            AlternativePositions = [.. alternatives]
        };

    public static Game Game(
        int id = 1,
        int seasonId = 1,
        GameSplitType split = GameSplitType.Halves,
        int durationMinutes = 60,
        DateTime? date = null) =>
        new()
        {
            Id = id,
            SeasonId = seasonId,
            Opponent = "Opponent",
            Date = date ?? new DateTime(2026, 3, 14),
            SplitType = split,
            GameDurationMinutes = durationMinutes
        };

    /// <summary>Adds a period with the given lineup. Ids are assigned in insertion order from 1.</summary>
    public static GamePeriod AddPeriod(this Game game, PeriodType type, params GamePlayerPosition[] lineup)
    {
        var period = new GamePeriod
        {
            Id = game.Periods.Count + 1,
            GameId = game.Id,
            PeriodType = type,
            PlayerPositions = [.. lineup]
        };

        foreach (var entry in period.PlayerPositions) entry.GamePeriodId = period.Id;

        game.Periods.Add(period);
        return period;
    }

    public static GamePlayerPosition Starter(int playerId, PlayerPosition position, int? slot = null) =>
        new() { PlayerId = playerId, Position = position, SlotIndex = slot, IsSubstitute = false };

    public static GamePlayerPosition Sub(int playerId, PlayerPosition position = PlayerPosition.CM) =>
        new() { PlayerId = playerId, Position = position, IsSubstitute = true };

    public static GameSubstitution Substitution(
        Game game, GamePeriod period, int offId, int onId, int atSeconds, PlayerPosition position, int? slot = null)
    {
        var sub = new GameSubstitution
        {
            Id = game.Substitutions.Count + 1,
            GameId = game.Id,
            GamePeriodId = period.Id,
            PlayerOffId = offId,
            PlayerOnId = onId,
            AtSeconds = atSeconds,
            Position = position,
            SlotIndex = slot
        };

        game.Substitutions.Add(sub);
        return sub;
    }

    public static GameGoal Goal(int gameId = 1, int? scorerId = null, int? assisterId = null,
        bool ownGoal = false, bool opponentGoal = false) =>
        new()
        {
            GameId = gameId,
            ScorerId = scorerId,
            AssisterId = assisterId,
            IsOwnGoal = ownGoal,
            IsOpponentGoal = opponentGoal
        };

    /// <summary>A squad of full members, unless the player id appears in <paramref name="guestIds"/>
    /// or <paramref name="injuredIds"/>.</summary>
    public static SeasonSquad Squad(
        int seasonId, IEnumerable<Player> players, int[]? guestIds = null, int[]? injuredIds = null) =>
        new(seasonId, players.Select(p => new SeasonSquadMember
        {
            SeasonId = seasonId,
            PlayerId = p.Id,
            Player = p,
            IsGuest = (guestIds ?? []).Contains(p.Id),
            IsInjured = (injuredIds ?? []).Contains(p.Id)
        }));
}
