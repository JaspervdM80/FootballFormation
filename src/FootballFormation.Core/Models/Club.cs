namespace FootballFormation.Core.Models;

public class Club
{
    /// The theme every club starts on. Names a preset in UI/Theming/ClubTheme — the styles themselves are not editable, so this column
    /// holds which one rather than what it looks like.
    public const string DefaultTheme = "GJS";

    public int Id { get; set; }

    public required string Name { get; set; }

    /// A path under wwwroot, so swapping a crest is a file drop. Null falls back to the theme's own logo.
    public string? LogoUrl { get; set; }

    public string ThemeName { get; set; } = DefaultTheme;

    public List<Team> Teams { get; set; } = [];
}
