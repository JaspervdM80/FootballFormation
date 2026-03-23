using MudBlazor;

namespace FootballFormation.UI.Layout;

public partial class MainLayout
{
    private static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#f59e0b",
            Secondary = "#14b8a6",
            AppbarBackground = "#1a1a2e"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#f59e0b",
            PrimaryContrastText = "#0c0c14",
            Secondary = "#14b8a6",
            Tertiary = "#fbbf24",
            AppbarBackground = "#13131f",
            AppbarText = "rgba(255,255,255,0.85)",
            Surface = "#141420",
            Background = "#0c0c14",
            DrawerBackground = "#13131f",
            DrawerText = "rgba(255,255,255,0.7)",
            TextPrimary = "rgba(255,255,255,0.87)",
            TextSecondary = "rgba(255,255,255,0.5)",
            ActionDefault = "rgba(255,255,255,0.5)",
            ActionDisabled = "rgba(255,255,255,0.2)",
            Divider = "rgba(255,255,255,0.06)",
            TableHover = "rgba(255,255,255,0.03)",
            TableStriped = "rgba(255,255,255,0.015)",
            LinesDefault = "rgba(255,255,255,0.08)",
            OverlayDark = "rgba(0,0,0,0.6)"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px"
        }
    };
}
