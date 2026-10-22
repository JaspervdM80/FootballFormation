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
    AlreadyInSquad,
    Invalid
}

public sealed record PastedPlayerLine(
    int LineNumber, string Text, string FirstName, string? Surname, int? ShirtNumber, PastedLineProblem Problem)
{
    public string DisplayName => Surname is not null ? $"{FirstName} {Surname}" : FirstName;

    public bool IsValid => Problem == PastedLineProblem.None;
}

/// <paramref name="Existing"/> is the player the line resolved to, for AddExisting and AlreadyInSquad.
public sealed record PastedSquadEntry(PastedPlayerLine Line, PastedLineAction Action, Player? Existing);

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

    /// Blank lines are dropped; every other line comes back, flagged when it cannot be added, so the preview can show it. A shirt number
    /// listed twice is the plan's to flag, since it depends on who is already on file.
    public static IReadOnlyList<PastedPlayerLine> Parse(string? text)
    {
        var lines = new List<PastedPlayerLine>();
        var rawLines = (text ?? "").Split('\n');

        for (var i = 0; i < rawLines.Length; i++)
        {
            var line = Collapse(rawLines[i]);
            if (line.Length == 0) continue;
            lines.Add(ParseLine(i + 1, line));
        }

        return FlagDuplicateNames(lines);
    }

    /// The same plan drives the preview and the write, so what the dialog promised is what gets saved. A name matches on the whole
    /// display name, ignoring case and spacing; <paramref name="earlierPlayers"/> should already leave out the archived and the squad.
    public static IReadOnlyList<PastedSquadEntry> Plan(
        IReadOnlyList<PastedPlayerLine> lines, IEnumerable<Player> squadPlayers, IEnumerable<Player> earlierPlayers)
    {
        var inSquad = ByName(squadPlayers);
        var earlier = ByName(earlierPlayers);

        var entries = lines.Select(line =>
                !line.IsValid ? new PastedSquadEntry(line, PastedLineAction.Invalid, null)
                : inSquad.TryGetValue(NameKey(line.DisplayName), out var member)
                    ? new PastedSquadEntry(line, PastedLineAction.AlreadyInSquad, member)
                : earlier.TryGetValue(NameKey(line.DisplayName), out var existing)
                    ? new PastedSquadEntry(line, PastedLineAction.AddExisting, existing)
                    : new PastedSquadEntry(line, PastedLineAction.Create, null))
            .ToList();

        // Only a created player takes the pasted number — someone already on file keeps her own — so only those can clash. Every line
        // sharing one is flagged, since nothing says which of the two is the mistake.
        var sharedNumbers = entries
            .Where(e => e.Action == PastedLineAction.Create && e.Line.ShirtNumber is not null)
            .GroupBy(e => e.Line.ShirtNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        return
        [
            .. entries.Select(e =>
                e.Action == PastedLineAction.Create && sharedNumbers.Contains(e.Line.ShirtNumber)
                    ? new PastedSquadEntry(e.Line with { Problem = PastedLineProblem.DuplicateShirtNumber }, PastedLineAction.Invalid, null)
                    : e)
        ];
    }

    private static PastedPlayerLine ParseLine(int lineNumber, string line)
    {
        int? shirtNumber = null;
        var name = line;
        var problem = PastedLineProblem.None;

        var numbered = NumberedLine().Match(line);
        if (numbered.Success)
        {
            name = numbered.Groups["name"].Value.Trim();
            if (int.TryParse(numbered.Groups["number"].Value, out var number)
                && number is >= MinShirtNumber and <= MaxShirtNumber)
            {
                shirtNumber = number;
            }
            else
            {
                problem = PastedLineProblem.ShirtNumberOutOfRange;
            }
        }

        var words = name.Split(' ', 2);
        var firstName = words[0];
        var surname = words.Length > 1 ? words[1] : null;
        if (firstName.Length == 0) problem = PastedLineProblem.MissingName;

        return new PastedPlayerLine(lineNumber, line, firstName, surname, shirtNumber, problem);
    }

    // Every line sharing a name is flagged, not just the later ones: nothing says which of the two is the mistake.
    private static List<PastedPlayerLine> FlagDuplicateNames(List<PastedPlayerLine> lines)
    {
        var sharedNames = lines
            .Where(l => l.IsValid)
            .GroupBy(l => NameKey(l.DisplayName))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        return
        [
            .. lines.Select(l => l.IsValid && sharedNames.Contains(NameKey(l.DisplayName))
                ? l with { Problem = PastedLineProblem.DuplicateName }
                : l)
        ];
    }

    private static Dictionary<string, Player> ByName(IEnumerable<Player> players)
    {
        var byName = new Dictionary<string, Player>();
        foreach (var player in players) byName.TryAdd(NameKey(player.DisplayName), player);
        return byName;
    }

    private static string NameKey(string name) => Collapse(name).ToUpperInvariant();

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();
}
