using FootballFormation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class MatchPreferencesConfiguration : IEntityTypeConfiguration<MatchPreferences>
{
    public void Configure(EntityTypeBuilder<MatchPreferences> entity)
    {
        entity.HasKey(m => m.Id);

        // Cascade, like SeasonSquadMember and unlike Season -> Game: a preferences row is
        // pure configuration with no history of its own, so it must not make an otherwise
        // game-free season undeletable.
        entity.HasOne(m => m.Season)
            .WithMany()
            .HasForeignKey(m => m.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per season. MatchPreferencesService creates it on first read; this is the
        // net underneath two circuits reading the same season at once.
        entity.HasIndex(m => m.SeasonId).IsUnique();
    }
}

internal sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> entity)
    {
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Username).IsRequired().HasMaxLength(50);
        entity.Property(a => a.PasswordHash).IsRequired();
    }
}
