using FootballFormation.UI.State;

namespace FootballFormation.UI.Pages;

public partial class ChooseTeam
{
    [Inject] private TeamState Team { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "redirectUri")] private string? RedirectUri { get; set; }

    private List<(Club Club, List<Team> Teams)> _clubs = [];

    private string ReturnUrl =>
        RedirectUri is { } uri && uri.StartsWith('/') && !uri.StartsWith("//")
            && !uri.StartsWith(AppRoutes.ChooseTeam, StringComparison.OrdinalIgnoreCase)
            ? uri
            : AppRoutes.Home;

    protected override async Task OnInitializedAsync()
    {
        await Team.EnsureLoadedAsync();

        _clubs = [.. Team.Teams
            .Where(t => t.Club is not null)
            .GroupBy(t => t.ClubId)
            .Select(g => (g.First().Club!, g.ToList()))];
    }
}
