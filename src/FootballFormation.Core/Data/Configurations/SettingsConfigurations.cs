using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class MatchPreferencesConfiguration : IEntityTypeConfiguration<MatchPreferences>
{
    public void Configure(EntityTypeBuilder<MatchPreferences> entity)
    {
        entity.HasKey(m => m.Id);

        entity.Property(m => m.TrainingDays).HasCsvListConversion();

        // Cascade, unlike Season -> Game: a preferences row is pure configuration with no history, so it must not make an otherwise
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

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> entity)
    {
        entity.ToTable("Users");

        entity.HasKey(u => u.Id);
        entity.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
        entity.Property(u => u.PasswordHash).IsRequired();
        entity.Property(u => u.Role).IsRequired();
        entity.Property(u => u.SecurityStamp).IsRequired().HasMaxLength(64);

        // Two accounts sharing a login would make the credential check ambiguous — it takes the
        // first match. UserService checks for a duplicate before writing; this is the net under it.
        entity.HasIndex(u => u.Username).IsUnique();

        // Restrict, as Club -> Team: deleting a team must not take the accounts that run it, which would revoke an admin without
        // passing the last-admin rule. TeamService refuses with a readable message instead.
        entity.HasOne<Team>()
            .WithMany()
            .HasForeignKey(u => u.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
