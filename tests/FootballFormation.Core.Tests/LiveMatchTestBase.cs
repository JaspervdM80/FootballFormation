using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FootballFormation.Core.Tests;

/// <summary>
/// What the live-match test classes share: a game laid out in periods with a lineup on the pitch,
/// and a reload that reads back what a service actually wrote.
/// <para>
/// The touchline is four services — reading the match, the clock, the goals and the substitutions —
/// and each has its own test class, but they are all about the same match being played, so the
/// fixture is described once here.
/// </para>
/// </summary>
public abstract class LiveMatchTestBase : ServiceTestBase
{
    protected static readonly DateTime KickOff = Now;

    /// <summary>A 60-minute game in halves with both periods laid out and a lineup on the pitch.</summary>
    protected async Task<Game> SeedGameAsync(GameSplitType split = GameSplitType.Halves)
    {
        var season = Season.CreateFor(KickOff);
        Db.Seasons.Add(season);

        var players = Enumerable.Range(1, 4)
            .Select(i => new Player { FirstName = $"P{i}", ShirtNumber = i, PreferredPosition = PlayerPosition.CM })
            .ToList();
        Db.Players.AddRange(players);
        await Db.SaveChangesAsync();

        var game = new Game
        {
            Opponent = "Opponent",
            Date = KickOff.Date,
            SeasonId = season.Id,
            SplitType = split,
            GameDurationMinutes = 60
        };

        foreach (var type in PeriodTypeExtensions.ForSplitType(split))
        {
            game.Periods.Add(new GamePeriod
            {
                PeriodType = type,
                PlayerPositions =
                [
                    new GamePlayerPosition { PlayerId = players[0].Id, Position = PlayerPosition.GK, SlotIndex = 0 },
                    new GamePlayerPosition { PlayerId = players[1].Id, Position = PlayerPosition.CM, SlotIndex = 5 },
                    new GamePlayerPosition { PlayerId = players[2].Id, Position = PlayerPosition.CM, IsSubstitute = true }
                ]
            });
        }

        Db.Games.Add(game);
        await Db.SaveChangesAsync();
        return game;
    }

    protected async Task<Game> ReloadAsync(int gameId)
    {
        Db.ChangeTracker.Clear();
        return await Db.Games.Include(g => g.Periods).FirstAsync(g => g.Id == gameId);
    }

    protected Task<List<Player>> PlayersAsync() => Db.Players.OrderBy(p => p.Id).ToListAsync();
}
