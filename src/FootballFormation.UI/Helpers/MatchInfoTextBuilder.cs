namespace FootballFormation.UI.Helpers;

/// The message sent round the team the days before a match — the fixture, the times, where to go and who has which duty. Beside
/// MatchSummaryTextBuilder, which does the same job for a match already played, and here for the same reason: it needs localized labels.
public static class MatchInfoTextBuilder
{
    /// <paramref name="ourTeamName"/> names our side in the header; everything left unset on the game is dropped, and a group whose lines
    /// are all missing takes its blank separator with it.
    public static string Build(Game game, string ourTeamName, IStringLocalizer<Strings> L)
    {
        var homeName = game.IsHomeGame ? ourTeamName : game.Opponent;
        var awayName = game.IsHomeGame ? game.Opponent : ourTeamName;

        var lines = new List<string>
        {
            $"⚽ {L["{0} vs {1}", homeName, awayName]}",
            "",
            $"📅 {game.Date:dd-MM-yyyy}"
        };

        AddGroup(lines, [
            TimeLine("🦺", game.MeetTime, game.IsHomeGame ? L["assemble"] : L["depart"]),
            TimeLine("⏱️", game.WarmUpTime, L["briefing/warm-up"]),
            game.HasStartTime ? $"⚽ {game.Date:HH:mm} {L["kick-off"]}" : null]);

        AddGroup(lines, [
            NamedLine("⚽", game.FieldName, field => L["field {0}", field]),
            NamedLine("🏠", game.DressingRoom, room => L["dressing room {0}", room]),
            DetailLine("🏟️", game.SportsPark),
            DetailLine("📍", game.City)]);

        AddGroup(lines, [
            DutyLine("🧹", L["Dressing room"], game.DressingRoomDuty),
            DutyLine("🚩", L["Flags"], game.FlagDuty),
            DutyLine("🧺", L["Kit wash"], game.WashDuty)], heading: $"{L["Duties"]}:");

        return string.Join('\n', lines);
    }

    private static string? NamedLine(string emoji, string? value, Func<string, string> name) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{emoji} {name(value.Trim())}";

    private static string? DetailLine(string emoji, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{emoji} {value.Trim()}";

    private static string? DutyLine(string emoji, string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{emoji} {label}: {value.Trim()}";

    private static string? TimeLine(string emoji, TimeSpan? time, string label) =>
        time is null ? null : $"{emoji} {time.Value:hh\\:mm} {label}";

    private static void AddGroup(List<string> lines, string?[] group, string? heading = null)
    {
        var present = group.OfType<string>().ToList();
        if (present.Count == 0) return;

        lines.Add("");
        if (heading is not null) lines.Add(heading);
        lines.AddRange(present);
    }
}
