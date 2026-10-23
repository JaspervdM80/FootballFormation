using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FootballFormation.Core.Services;

// On SaveChanges rather than the service shape, so a new write method invalidates by writing. ExecuteUpdate, ExecuteDelete and raw SQL
// would go behind its back; the app uses none outside the migrations.
public sealed class StatsCacheInvalidator(StatsCache cache) : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<IModel, HashSet<Type>> TeamOwnedTypesByModel = new();

    // Decided before the save, while the change tracker still says what is being written; applied after, once it has been.
    private readonly ConditionalWeakTable<DbContext, StrongBox<int?>> _pendingTeam = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Remember(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Invalidate(eventData.Context, result);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Invalidate(eventData.Context, result);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Remember(DbContext? context)
    {
        if (context is not null) _pendingTeam.AddOrUpdate(context, new StrongBox<int?>(OnlyTeamWrittenTo(context)));
    }

    // Zero rows is a save over an unchanged context — no write to invalidate for.
    private void Invalidate(DbContext? context, int rowsAffected)
    {
        if (rowsAffected == 0) return;

        if (context is not null && _pendingTeam.TryGetValue(context, out var team) && team.Value is { } teamId)
            cache.Invalidate(teamId);
        else
            cache.Invalidate();
    }

    // Null unless every row being written is provably that one team's. A player is the club's and reaches every team's figures, and a
    // type nobody has classified is assumed to as well — the safe mistake is dropping too much, never serving a stale figure.
    private static int? OnlyTeamWrittenTo(DbContext context)
    {
        if (context is not AppDbContext { CurrentTeamId: { } teamId } db) return null;

        var teamOwned = TeamOwnedTypesByModel.GetValue(db.Model, TeamOwnedTypes);

        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (!teamOwned.Contains(entry.Metadata.ClrType)) return null;

            if (entry.Metadata.FindProperty(nameof(Game.TeamId)) is { } column
                && (!Equals(entry.Property(column.Name).CurrentValue, teamId) || !Equals(entry.Property(column.Name).OriginalValue, teamId)))
                return null;
        }

        return teamId;
    }

    // A type filtered on its own TeamId, or one hanging from such a type by a required key — a goal from its game, a line-up from its period.
    private static HashSet<Type> TeamOwnedTypes(IModel model)
    {
        var entityTypes = model.GetEntityTypes().ToList();
        var owned = entityTypes
            .Where(type => type.FindProperty(nameof(Game.TeamId)) is not null && type.GetDeclaredQueryFilters().Count > 0)
            .Select(type => type.ClrType)
            .ToHashSet();

        bool grew;
        do
        {
            grew = false;
            foreach (var type in entityTypes.Where(type => !owned.Contains(type.ClrType)))
            {
                if (type.GetForeignKeys().Any(fk => fk.IsRequired && owned.Contains(fk.PrincipalEntityType.ClrType)))
                    grew = owned.Add(type.ClrType) || grew;
            }
        }
        while (grew);

        return owned;
    }
}
