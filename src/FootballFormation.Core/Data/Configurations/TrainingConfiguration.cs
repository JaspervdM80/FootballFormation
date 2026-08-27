using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class TrainingConfiguration : IEntityTypeConfiguration<Training>
{
    public void Configure(EntityTypeBuilder<Training> entity)
    {
        entity.HasKey(t => t.Id);

        entity.Property(t => t.Notes).HasMaxLength(2000);

        entity.Property(t => t.UnavailablePlayerIds).HasCsvListConversion();

        // Restrict, like Season -> Game: a session records who was absent, so deleting a season must not take a year of attendance with
        // it. SeasonService.DeleteAsync refuses with a readable message rather than letting the caller hit the raw constraint.
        // HasOne<Season>() rather than HasOne(t => t.Season): a Training carries the id and no navigation, as GameInjury does.
        entity.HasOne<Season>()
            .WithMany()
            .HasForeignKey(t => t.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Not unique on (SeasonId, Date): two sessions on one day is an ordinary week.
        entity.HasIndex(t => t.SeasonId);
    }
}
