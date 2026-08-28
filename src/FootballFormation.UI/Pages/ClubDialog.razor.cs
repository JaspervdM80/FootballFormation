namespace FootballFormation.UI.Pages;

public partial class ClubDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    /// Null when adding a club rather than editing one.
    [Parameter] public Club? Club { get; set; }

    private MudForm Form { get; set; } = null!;

    private string Name { get; set; } = string.Empty;
    private string LogoUrl { get; set; } = string.Empty;
    private string ThemeName { get; set; } = FootballFormation.Core.Models.Club.DefaultTheme;

    protected override void OnParametersSet()
    {
        if (Club is null) return;

        Name = Club.Name;
        LogoUrl = Club.LogoUrl ?? string.Empty;
        ThemeName = Club.ThemeName;
    }

    private async Task Submit()
    {
        await Form.ValidateAsync();
        if (!Form.IsValid) return;

        MudDialog.Close(DialogResult.Ok(new Model { Name = Name, LogoUrl = LogoUrl, ThemeName = ThemeName }));
    }

    private void Cancel() => MudDialog.Cancel();

    public sealed class Model
    {
        public string Name { get; init; } = string.Empty;
        public string LogoUrl { get; init; } = string.Empty;
        public string ThemeName { get; init; } = string.Empty;
    }
}
