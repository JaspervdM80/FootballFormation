using System.Text.RegularExpressions;

namespace FootballFormation.Core.Models;

public enum PastedLineProblem
{
    None,
    MissingName,
    ShirtNumberOutOfRange,
    DuplicateShirtNumber,
    DuplicateName
}

public enum PastedLineAction
{
    Create,
    AddExisting,
    AlreadyInSquad
}

/// <paramref name="Player"/> is who the line becomes: a new, unsaved player for Create, otherwise the one already on file.
public sealed record PastedSquadEntry(
    int LineNumber, string Text, Player Player, PastedLineAction Action, PastedLineProblem Problem)
{
    public bool IsValid => Problem == PastedLineProblem.None;
}

public sealed record PastedSquadOutcome(
    IReadOnlyList<Player> Created, IReadOnlyList<Player> AddedFromEarlierSeason, IReadOnlyList<Player> AlreadyInSquad)
{
    public int Added => Created.Count + AddedFromEarlierSeason.Count;
}

/// One player per line with an optional leading shirt number — what a coach pastes from a team app or a spreadsheet column.
public static partial class PastedSquadList
{
    public const int MinShirtNumber = 1;
    public const int MaxShirtNumber = 99;

    [GeneratedRegex(@"^#?(?<number>\d+)(?:[.):]\s*|\s+|$)(?<name>.*)$")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// The same plan drives the preview and the write, so what the dialog promised is what gets saved. Blank lines are dropped; every
    /// other line comes back, flagged when it cannot be added. A name matches on the whole display name, ignoring case and spacing;
    /// <paramref name="earlierPlayers"/> should already leave out the archived and the squad.
    public static IReadOnlyList<PastedSquadEntry> Plan(
        string? text, IEnumerable<Player> squadPlayers, IEnumerable<Player> earlierPlayers)
    {
        var inSquad = ByName(squadPlayers);
        var earlier = ByName(earlierPlayers);

        var entries = (text ?? "").Split('\n')
            .Select((raw, i) => (LineNumber: i + 1, Text: Collapse(raw)))
            .Where(l => l.Text.Length > 0)
            .Select(l => Resolve(l.LineNumber, l.Text, inSquad, earlier))
            .ToList();

        // Every line sharing a name or a new player's number is flagged, not just the later ones: nothing says which is the mistake.
        // Only a created player takes the pasted number — someone already on file keeps her own — so only those can clash.
        var valid = entries.Where(e => e.IsValid).ToList();
        var sharedNames = Shared(valid.Select(e => NameKey(e.Player.DisplayName)));
        var sharedNumbers = Shared(valid
            .Where(e => e.Action == PastedLineAction.Create && e.Player.ShirtNumber is not null)
            .Select(e => e.Player.ShirtNumber!.Value));

        return
        [
            .. entries.Select(e =>
                !e.IsValid ? e
                : sharedNames.Contains(NameKey(e.Player.DisplayName)) ? e with { Problem = PastedLineProblem.DuplicateName }
                : e.Action == PastedLineAction.Create && e.Player.ShirtNumber is { } n && sharedNumbers.Contains(n)
                    ? e with { Problem = PastedLineProblem.DuplicateShirtNumber }
                    : e)
        ];
    }

    private static PastedSquadEntry Resolve(
        int lineNumber, string text, Dictionary<string, Player> inSquad, Dictionary<string, Player> earlier)
    {
        int? shirtNumber = null;
        var name = text;
        var problem = PastedLineProblem.None;

        var numbered = NumberedLine().Match(text);
        if (numbered.Success)
        {
            name = numbered.Groups["name"].Value.Trim();
            if (int.TryParse(numbered.Groups["number"].Value, out var number)
                && number is >= MinShirtNumber and <= MaxShirtNumber)
                shirtNumber = number;
            else
                problem = PastedLineProblem.ShirtNumberOutOfRange;
        }

        var words = name.Split(' ', 2);
        if (words[0].Length == 0) problem = PastedLineProblem.MissingName;

        var created = new Player { FirstName = words[0], Surname = words.Length > 1 ? words[1] : null, ShirtNumber = shirtNumber };
        var key = NameKey(created.DisplayName);

        return inSquad.TryGetValue(key, out var member)
            ? new PastedSquadEntry(lineNumber, text, member, PastedLineAction.AlreadyInSquad, problem)
            : earlier.TryGetValue(key, out var existing)
                ? new PastedSquadEntry(lineNumber, text, existing, PastedLineAction.AddExisting, problem)
                : new PastedSquadEntry(lineNumber, text, created, PastedLineAction.Create, problem);
    }

    private static HashSet<T> Shared<T>(IEnumerable<T> keys) =>
        keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

    private static Dictionary<string, Player> ByName(IEnumerable<Player> players)
    {
        var byName = new Dictionary<string, Player>();
        foreach (var player in players) byName.TryAdd(NameKey(player.DisplayName), player);
        return byName;
    }

    private static string NameKey(string name) => Collapse(name).ToUpperInvariant();

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();
}
