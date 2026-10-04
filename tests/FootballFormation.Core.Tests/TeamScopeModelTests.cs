using Microsoft.EntityFrameworkCore.Metadata;

namespace FootballFormation.Core.Tests;

/// The structural half of <see cref="TeamDataScopingTests"/>: that suite proves the reads written so far stay in their team, this one
/// reads the EF model, so a new entity that forgot its filter fails here the day it is mapped rather than the day a read is written.
public class TeamScopeModelTests
{
    private static readonly string[] ScopeColumns = ["SeasonId", "TeamId", "ClubId"];

    private static readonly Dictionary<string, string> UnfilteredByDesign = new()
    {
        [nameof(AppUser)] = "sign-in finds an account before any team is in scope, and an application admin's account names none",
        [nameof(Team)] = "the team picker and CurrentTeam resolve against every club's teams",
    };

    private static readonly Dictionary<string, string> CascadesFromTeamByDesign = new()
    {
        [nameof(PushSubscription)] = "a browser's opt-in, with no history to keep, goes with its team",
    };

    [Fact]
    public void Every_entity_carrying_a_season_team_or_club_has_a_query_filter()
    {
        var unfiltered = EntityTypes()
            .Where(type => ScopeColumns.Any(column => type.FindProperty(column) is not null))
            .Where(type => type.GetDeclaredQueryFilters().Count == 0)
            .Select(type => type.ClrType.Name)
            .Where(name => !UnfilteredByDesign.ContainsKey(name));

        Assert.Empty(unfiltered);
    }

    [Fact]
    public void A_team_or_club_delete_has_no_silent_cascade_through_its_data()
    {
        var cascading = EntityTypes()
            .SelectMany(type => type.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Team) || fk.PrincipalEntityType.ClrType == typeof(Club))
            .Where(fk => fk.DeleteBehavior != DeleteBehavior.Restrict)
            .Select(fk => fk.DeclaringEntityType.ClrType.Name)
            .Where(name => !CascadesFromTeamByDesign.ContainsKey(name));

        Assert.Empty(cascading);
    }

    [Fact]
    public void The_scan_sees_the_filters_and_restrictions_it_is_meant_to_judge()
    {
        // The presence twin of the two above: a renamed scope column or a model that stopped loading would leave them nothing to find.
        var types = EntityTypes();
        var scoped = types.Count(type => ScopeColumns.Any(column => type.FindProperty(column) is not null));
        var filtered = types.Count(type => type.GetDeclaredQueryFilters().Count > 0);
        var restricted = types.SelectMany(type => type.GetForeignKeys())
            .Count(fk => fk.PrincipalEntityType.ClrType == typeof(Team) && fk.DeleteBehavior == DeleteBehavior.Restrict);

        Assert.True(scoped > 5 && filtered > 5 && restricted > 0, $"{scoped} scoped, {filtered} filtered, {restricted} restricted — has the scan stopped matching?");
        Assert.Contains(types, type => type.ClrType == typeof(Season) && type.GetDeclaredQueryFilters().Count > 0);
    }

    [Fact]
    public void Every_exemption_still_names_an_entity_it_exempts()
    {
        var names = EntityTypes().Select(type => type.ClrType.Name).ToHashSet();
        var stale = UnfilteredByDesign.Keys.Concat(CascadesFromTeamByDesign.Keys).Where(name => !names.Contains(name)).ToList();

        Assert.Empty(stale);
    }

    private static List<IEntityType> EntityTypes()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        return [.. db.Model.GetEntityTypes().Where(type => !type.IsOwned())];
    }
}
