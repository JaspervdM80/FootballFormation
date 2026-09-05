namespace FootballFormation.Core.Tests;

/// The window arithmetic runs on the loaded rows rather than as SQL — why it moved is pinned in <see cref="GameOrderingTests"/>.
public class SeasonOrderingTests : ServiceTestBase
{
    [Fact]
    public async Task Seasons_come_back_newest_first()
    {
        await SeedSeasonsAsync(2024, 2025, 2026);

        var seasons = (await Seasons.GetAllAsync()).Value!;

        Assert.Equal(
            new[] { 2026, 2025, 2024 },
            seasons.Select(s => s.StartDate.Year).ToArray());
    }

    [Fact]
    public async Task A_date_resolves_to_the_season_whose_window_covers_it()
    {
        await SeedSeasonsAsync(2025, 2026);

        var season = (await Seasons.FindForDateAsync(new DateTime(2026, 3, 14))).Value;

        Assert.Equal(2025, season!.StartDate.Year);
    }

    [Fact]
    public async Task Both_ends_of_a_window_belong_to_it()
    {
        await SeedSeasonsAsync(2025);

        var opening = (await Seasons.FindForDateAsync(new DateTime(2025, 7, 1))).Value;
        var closing = (await Seasons.FindForDateAsync(new DateTime(2026, 6, 30))).Value;

        Assert.NotNull(opening);
        Assert.NotNull(closing);
    }

    [Fact]
    public async Task A_date_outside_every_window_resolves_to_nothing()
    {
        await SeedSeasonsAsync(2025);

        var season = (await Seasons.FindForDateAsync(new DateTime(2030, 3, 14))).Value;

        Assert.Null(season);
    }

    /// A full July–June window would straddle the seasons either side of a narrower hole, so GetOrCreateForDateAsync clamps it.
    [Fact]
    public async Task A_season_created_for_a_date_in_a_gap_is_clamped_to_its_neighbours()
    {
        var teamId = EnsureScopedTeam();
        Db.Seasons.AddRange(
            new Season
            {
                Name = "Before",
                TeamId = teamId,
                StartDate = new DateTime(2025, 7, 1),
                EndDate = new DateTime(2026, 4, 30)
            },
            new Season
            {
                Name = "After",
                TeamId = teamId,
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2027, 6, 30)
            });
        await Db.SaveChangesAsync();

        var created = (await Seasons.GetOrCreateForDateAsync(new DateTime(2026, 5, 15))).Value!;

        Assert.Equal(new DateTime(2026, 5, 1), created.StartDate.Date);
        Assert.Equal(new DateTime(2026, 6, 30), created.EndDate.Date);
    }

    [Fact]
    public async Task The_previous_season_is_the_newest_one_starting_before_this_one()
    {
        var seasons = await SeedSeasonsAsync(2024, 2025, 2026);

        var previous = (await Squads.FindPreviousSeasonAsync(seasons[2026].Id)).Value;

        Assert.Equal(2025, previous!.StartDate.Year);
    }

    [Fact]
    public async Task The_oldest_season_has_no_previous_one()
    {
        var seasons = await SeedSeasonsAsync(2024, 2025);

        var result = await Squads.FindPreviousSeasonAsync(seasons[2024].Id);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /// Straight to the database: going through CreateAsync would enforce the gapless rule, which is not what these tests are about.
    private async Task<Dictionary<int, Season>> SeedSeasonsAsync(params int[] startYears)
    {
        var teamId = EnsureScopedTeam();
        var seasons = startYears
            .Select(year =>
            {
                var season = Season.CreateFor(new DateTime(year, Season.StartMonth, 1));
                season.TeamId = teamId;
                return season;
            })
            .ToList();

        Db.Seasons.AddRange(seasons);
        await Db.SaveChangesAsync();

        return seasons.ToDictionary(s => s.StartDate.Year, s => s);
    }
}
