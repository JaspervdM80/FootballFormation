using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> entity)
    {
        entity.HasKey(g => g.Id);
        entity.Property(g => g.Opponent).IsRequired().HasMaxLength(100);

        entity.Property(g => g.DressingRoom).HasMaxLength(50);
        entity.Property(g => g.FieldName).HasMaxLength(50);
        entity.Property(g => g.SportsPark).HasMaxLength(100);
        entity.Property(g => g.City).HasMaxLength(100);
        entity.Property(g => g.DressingRoomDuty).HasMaxLength(100);
        entity.Property(g => g.FlagDuty).HasMaxLength(100);
        entity.Property(g => g.WashDuty).HasMaxLength(100);

        entity.Property(g => g.UnavailablePlayerIds).HasCsvListConversion();
        entity.Property(g => g.InjuredPlayerIds).HasCsvListConversion();
        entity.Property(g => g.GuestPlayerIds).HasCsvListConversion();

        // Restrict, as Season -> Game: the denormalised team FK must not give a team delete a second, silent path through the season's
        // games. Id only, no navigation, like AppUser -> Team.
        entity.HasOne<Team>()
            .WithMany()
            .HasForeignKey(g => g.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasMany(g => g.Periods)
            .WithOne(p => p.Game)
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(g => g.Goals)
            .WithOne(gl => gl.Game)
            .HasForeignKey(gl => gl.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(g => g.Substitutions)
            .WithOne(s => s.Game)
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // WithOne() rather than WithOne(i => i.Game): GameInjury carries the id without a
        // navigation back, so this side is the only one that names the pair.
        entity.HasMany(g => g.Injuries)
            .WithOne()
            .HasForeignKey(i => i.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(g => g.Comments)
            .WithOne(c => c.Game)
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
