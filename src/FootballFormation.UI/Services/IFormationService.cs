using FootballFormation.UI.Models;

namespace FootballFormation.UI.Services;

public interface IFormationService
{
    IReadOnlyList<Player> Players { get; }
    IReadOnlyList<Formation> Formations { get; }
    IReadOnlyList<Substitution> Substitutions { get; }
    void LoadPlayers(List<Player> players);
}