using FootballFormation.UI.Theming;

namespace FootballFormation.UI.Pages;

public partial class StyleGuide
{
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private static string ChipStyle(TokenKind kind, string token) => kind switch
    {
        TokenKind.Radius => $"border-radius: var({token}); background: var(--club-accent);",
        TokenKind.Size => $"width: var({token}); height: var({token}); background: var(--club-accent);",
        _ => $"background: var({token});",
    };
}
