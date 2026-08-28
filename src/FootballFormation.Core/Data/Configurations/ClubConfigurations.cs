using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> entity)
    {
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
        entity.Property(c => c.LogoUrl).HasMaxLength(255);
        entity.Property(c => c.ThemeName).IsRequired().HasMaxLength(50);

        entity.HasIndex(c => c.Name).IsUnique();

        // Restrict, as Season -> Game: a club is the top of the tree, and deleting one must never take its teams with it. TeamService
        // refuses with a readable message instead.
        entity.HasMany(c => c.Teams)
            .WithOne(t => t.Club)
            .HasForeignKey(t => t.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> entity)
    {
        entity.HasKey(t => t.Id);
        entity.Property(t => t.Name).IsRequired().HasMaxLength(50);

        // One "MO15-2" per club, though two clubs may both have one. TeamService checks before writing; this is the net under it.
        entity.HasIndex(t => new { t.ClubId, t.Name }).IsUnique();
    }
}
