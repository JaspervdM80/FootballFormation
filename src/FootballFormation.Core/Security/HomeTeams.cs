namespace FootballFormation.Core.Security;

/// Stands in for an absent ff.team cookie — a view choice, never authority. An entry naming no positive id is ignored rather than failing the boot.
public sealed class HomeTeams
{
    private readonly Dictionary<string, int> _byHost = new(StringComparer.OrdinalIgnoreCase);

    public HomeTeams(IEnumerable<KeyValuePair<string, string?>> entries)
    {
        foreach (var (host, value) in entries)
        {
            if (int.TryParse(value, out var teamId) && teamId > 0) _byHost[host] = teamId;
        }
    }

    public int? For(string host) => _byHost.TryGetValue(host, out var teamId) ? teamId : null;
}
