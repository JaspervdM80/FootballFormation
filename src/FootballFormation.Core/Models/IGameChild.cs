namespace FootballFormation.Core.Models;

// A row reached through its game and carrying no team of its own, so a write finding one by its own id has to ask the game.
public interface IGameChild
{
    int Id { get; }

    int GameId { get; }
}
