using FootballFormation.Models;

namespace FootballFormation.Services;

public interface IFormationService
{
    IReadOnlyList<Player> Players { get; }
    IReadOnlyList<Formation> Formations { get; }
    IReadOnlyList<Substitution> Substitutions { get; }
    void LoadPlayers(List<Player> players);
}