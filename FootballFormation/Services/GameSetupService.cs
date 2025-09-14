using FootballFormation.Managers;
using FootballFormation.Models;
using System.Collections.Generic;

namespace FootballFormation.Services;

public interface IGameSetupService
{
    List<GameSetup> GameSetups { get; }
    GameSetup? SelectedGameSetup { get; }
    void GenerateGameSetups(List<Player> players);
    void SelectGameSetup(int setupId);
}

public class GameSetupService : IGameSetupService
{
    private readonly GameSetupGenerator _generator;
    private readonly List<GameSetup> _gameSetups = [];
    private GameSetup? _selectedGameSetup;

    public List<GameSetup> GameSetups => _gameSetups;
    public GameSetup? SelectedGameSetup => _selectedGameSetup;

    public GameSetupService(
        ISquadManager squadManager,
        IPlayingTimeManager playingTimeManager,
        ISubstitutionManager substitutionManager,
        IFormationBuilder formationBuilder)
    {
        _generator = new GameSetupGenerator(squadManager, playingTimeManager, substitutionManager, formationBuilder);
    }

    public void GenerateGameSetups(List<Player> players)
    {
        _gameSetups.Clear();
        _selectedGameSetup = null;

        var generatedSetups = _generator.GenerateGameSetups(players);
        _gameSetups.AddRange(generatedSetups);

        // Automatically select the best setup (first one after sorting)
        if (_gameSetups.Any())
        {
            _selectedGameSetup = _gameSetups.First();
        }
    }

    public void SelectGameSetup(int setupId)
    {
        _selectedGameSetup = _gameSetups.FirstOrDefault(gs => gs.SetupId == setupId);
    }
}