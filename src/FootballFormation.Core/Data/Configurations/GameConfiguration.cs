using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> entity)
    {
        entity.HasKey(g => g.Id);
        entity.Property(g => g.Opponent).IsRequired().HasMaxLength(100);

        entity.Property(g => g.UnavailablePlayerIds).HasCsvListConversion();
        entity.Property(g => g.GuestPlayerIds).HasCsvListConversion();

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

        entity.HasMany(g => g.Comments)
            .WithOne(c => c.Game)
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
