namespace FootballFormation.Core.Tests;

/// Every date must map to exactly one season, so an overlap and a gap break the same invariant — a date inside a hole belongs to no
/// season at all, and the game dialog then offers an empty squad for it.
public class SeasonServiceTests : ServiceTestBase
{
    [Fact]
    public async Task A_season_needs_a_name()
    {
        var result = await Seasons.CreateAsync(new Season
        {
            Name = "  ",
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2027, 6, 30)
        });

        Assert.True(result.IsFailure);
        Assert.Empty(Read().Seasons);
    }

    [Fact]
    public async Task A_season_must_end_after_it_starts()
    {
        var result = await Seasons.CreateAsync(new Season
        {
            Name = "Backwards",
            StartDate = new DateTime(2027, 6, 30),
            EndDate = new DateTime(2026, 7, 1)
        });

        Assert.True(result.IsFailure);
        Assert.Empty(Read().Seasons);
    }

    [Fact]
    public async Task A_season_cannot_overlap_another()
    {
        var existing = await SeedSeasonAsync();

        var result = await Seasons.CreateAsync(new Season
        {
            Name = "Overlapping",
            StartDate = existing.StartDate.AddMonths(3),
            EndDate = existing.EndDate.AddMonths(3)
        });

        Assert.True(result.IsFailure);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task A_season_cannot_leave_a_gap_after_the_previous_one()
    {
        var existing = await SeedSeasonAsync();

        var result = await Seasons.CreateAsync(new Season
        {
            Name = "Late starter",
            StartDate = existing.EndDate.AddDays(8),
            EndDate = existing.EndDate.AddYears(1)
        });

        Assert.True(result.IsFailure);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task A_season_cannot_leave_a_gap_before_the_following_one()
    {
        var existing = await SeedSeasonAsync();

        var result = await Seasons.CreateAsync(new Season
        {
            Name = "Early finisher",
            StartDate = existing.StartDate.AddYears(-1),
            EndDate = existing.StartDate.AddDays(-8)
        });

        Assert.True(result.IsFailure);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task A_season_that_butts_up_against_its_neighbour_is_accepted()
    {
        var existing = await SeedSeasonAsync();

        var result = await Seasons.CreateAsync(new Season
        {
            Name = "Next",
            StartDate = existing.EndDate.AddDays(1),
            EndDate = existing.EndDate.AddYears(1)
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, Read().Seasons.Count());
    }

    [Fact]
    public async Task Editing_a_season_does_not_count_as_overlapping_itself()
    {
        var season = await SeedSeasonAsync();

        season.Name = "Renamed";
        var result = await Seasons.UpdateAsync(season);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", Read().Seasons.Single().Name);
    }

    [Fact]
    public async Task Exactly_one_season_is_current_after_switching()
    {
        var first = await SeedSeasonAsync();
        var second = (await Seasons.CreateAsync(new Season
        {
            Name = "Next",
            StartDate = first.EndDate.AddDays(1),
            EndDate = first.EndDate.AddYears(1)
        })).Value!;

        await Seasons.SetCurrentAsync(second.Id);

        var seasons = Read().Seasons.ToList();
        Assert.Equal(second.Id, Assert.Single(seasons, s => s.IsCurrent).Id);
    }

    [Fact]
    public async Task A_season_with_games_cannot_be_deleted()
    {
        var season = await SeedSeasonAsync();
        await Games.CreateAsync(TestData.Game(id: 0, seasonId: season.Id));

        var result = await Seasons.DeleteAsync(season.Id);

        Assert.True(result.IsFailure);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task A_season_with_only_trainings_cannot_be_deleted_either()
    {
        var season = await SeedSeasonAsync(isCurrent: false);
        await Trainings.CreateAsync(new Training { SeasonId = season.Id, Date = Now });

        var result = await Seasons.DeleteAsync(season.Id);

        // The Training FK is Restrict too, so without this guard the caller would hit a raw DbUpdateException instead of a sentence.
        Assert.True(result.IsFailure);
        Assert.Equal("Season {0} still has {1} trainings", result.ErrorKey);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task A_season_holding_nothing_but_its_generated_schedule_can_still_be_deleted()
    {
        var season = await SeedSeasonAsync(isCurrent: false);
        await SaveTrainingPeriodAsync(season.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 31));
        Assert.NotEmpty(Read().Trainings);

        var result = await Seasons.DeleteAsync(season.Id);

        // Setting a training period must not be the thing that locks a season in place: those evenings carry nothing, so they are the
        // schedule's rather than anybody's data, and they go with it.
        Assert.True(result.IsSuccess);
        Assert.Empty(Read().Seasons);
        Assert.Empty(Read().Trainings);
    }

    [Fact]
    public async Task A_generated_session_somebody_wrote_on_still_holds_its_season_back()
    {
        var season = await SeedSeasonAsync(isCurrent: false);
        await SaveTrainingPeriodAsync(season.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 31));

        var noted = Read().Trainings.ToList().First();
        noted.Notes = "Partijvorm";
        await Trainings.UpdateAsync(noted);

        var result = await Seasons.DeleteAsync(season.Id);

        // One note is enough: the count in the message is of the evenings that carry something, not of the whole generated run.
        Assert.True(result.IsFailure);
        Assert.Equal("Season {0} still has {1} trainings", result.ErrorKey);
        Assert.Single(Read().Seasons);
    }

    private async Task SaveTrainingPeriodAsync(int seasonId, DateTime first, DateTime last)
    {
        var prefs = (await Preferences.GetAsync(seasonId)).Value!;
        prefs.TrainingDays = [DayOfWeek.Tuesday];
        prefs.FirstTrainingDate = first;
        prefs.LastTrainingDate = last;

        Assert.True((await Preferences.SaveAsync(prefs)).IsSuccess);
    }

    [Fact]
    public async Task The_current_season_cannot_be_deleted()
    {
        var season = await SeedSeasonAsync(isCurrent: true);

        var result = await Seasons.DeleteAsync(season.Id);

        Assert.True(result.IsFailure);
        Assert.Single(Read().Seasons);
    }

    [Fact]
    public async Task Closing_gaps_pulls_a_later_seasons_start_back_to_meet_the_previous_one()
    {
        // Written straight to the database: CreateAsync would refuse the gap, and this repairs
        // databases that predate that check.
        var first = await SeedSeasonAsync(isCurrent: false);
        Db.Seasons.Add(new Season
        {
            Name = "Gapped",
            TeamId = first.TeamId,
            StartDate = first.EndDate.AddDays(30),
            EndDate = first.EndDate.AddYears(1),
            IsCurrent = true
        });
        await Db.SaveChangesAsync();

        var closed = await Seasons.CloseSeasonGapsAsync();

        Assert.Equal(1, closed.Value);
        var repaired = Read().Seasons.ToList().NewestFirst().First();
        Assert.Equal(first.EndDate.Date.AddDays(1), repaired.StartDate.Date);
    }

    [Fact]
    public async Task Closing_gaps_is_idempotent_and_leaves_a_healthy_database_alone()
    {
        var first = await SeedSeasonAsync(isCurrent: false);
        await Seasons.CreateAsync(new Season
        {
            Name = "Next",
            StartDate = first.EndDate.AddDays(1),
            EndDate = first.EndDate.AddYears(1)
        });

        // It runs on every boot, so doing nothing when there is nothing to do is the normal case.
        Assert.Equal(0, (await Seasons.CloseSeasonGapsAsync()).Value);
    }

    [Fact]
    public async Task An_empty_database_gets_a_current_season()
    {
        // Empty of seasons, not of teams: a deployment always has a team by the time this boot step runs.
        SeedTeam();
        var season = await Seasons.EnsureCurrentSeasonAsync();

        Assert.True(season.IsSuccess);
        Assert.True(Read().Seasons.Single().IsCurrent);
    }

    [Fact]
    public async Task A_database_with_no_current_season_promotes_the_newest_one()
    {
        var older = await SeedSeasonAsync(covering: Now.AddYears(-1), isCurrent: false);
        Db.Seasons.Add(new Season
        {
            Name = "Newer",
            TeamId = older.TeamId,
            StartDate = older.EndDate.AddDays(1),
            EndDate = older.EndDate.AddYears(1),
            IsCurrent = false
        });
        await Db.SaveChangesAsync();

        var season = await Seasons.EnsureCurrentSeasonAsync();

        Assert.Equal("Newer", season.Value!.Name);
        Assert.Single(Read().Seasons.ToList(), s => s.IsCurrent);
    }

    [Fact]
    public async Task Every_team_gets_its_own_current_season_on_boot()
    {
        // The boot form loops all teams, not only the one an absent cookie resolves — a fresh install with two teams must leave each
        // with a season to fall back on.
        var a = SeedTeam("Club A", "MO15-2");
        var b = SeedTeam("Club B", "MO17-1");

        Assert.True((await Seasons.EnsureEveryTeamHasCurrentSeasonAsync()).IsSuccess);

        var current = await Read().Seasons.IgnoreQueryFilters().Where(s => s.IsCurrent).ToListAsync();
        Assert.Equal(2, current.Count);
        Assert.Contains(current, s => s.TeamId == a.Id);
        Assert.Contains(current, s => s.TeamId == b.Id);
    }

    [Fact]
    public async Task Closing_gaps_for_every_team_repairs_each_team_within_its_own_chain()
    {
        var a = SeedTeam("Club A", "MO15-2");
        var first = await SeedSeasonAsync(isCurrent: false);
        Db.Seasons.Add(new Season
        {
            Name = "Gapped",
            TeamId = a.Id,
            StartDate = first.EndDate.AddDays(30),
            EndDate = first.EndDate.AddYears(1),
            IsCurrent = true
        });
        // A second team with a single, gapless season — its lone window must not be read against the other team's.
        SeedTeam("Club B", "MO17-1");
        await SeedSeasonAsync();
        await Db.SaveChangesAsync();

        var closed = await Seasons.CloseSeasonGapsForEveryTeamAsync();

        Assert.Equal(1, closed.Value);
    }

    [Fact]
    public async Task Updating_a_season_that_belongs_to_another_team_is_refused()
    {
        var a = SeedTeam("Club A", "MO15-2");
        var aSeason = await SeedSeasonAsync();

        // Now looking at another team; the detached Update must not reach across to team A's season by id.
        var b = SeedTeam("Club B", "MO17-1");
        var result = await Seasons.UpdateAsync(new Season
        {
            Id = aSeason.Id, Name = "Hijacked", TeamId = b.Id,
            StartDate = aSeason.StartDate, EndDate = aSeason.EndDate
        });

        Assert.True(result.IsFailure);
        var stored = await Read().Seasons.IgnoreQueryFilters().FirstAsync(s => s.Id == aSeason.Id);
        Assert.NotEqual("Hijacked", stored.Name);
        Assert.Equal(a.Id, stored.TeamId);
    }
}
