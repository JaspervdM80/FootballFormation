using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> entity)
    {
        entity.HasKey(s => s.Id);

        entity.Property(s => s.Endpoint).IsRequired().HasMaxLength(1000);
        entity.Property(s => s.P256dh).IsRequired().HasMaxLength(200);
        entity.Property(s => s.Auth).IsRequired().HasMaxLength(100);
        entity.Property(s => s.Culture).IsRequired().HasMaxLength(10);

        // Cascade, unlike the denormalised team FK on MatchPreferences: this is the only parent a subscription has, and a row carrying
        // no history must not be what makes a team undeletable.
        entity.HasOne<Team>()
            .WithMany()
            .HasForeignKey(s => s.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // The push service already guarantees one endpoint per browser, so a second row is a re-subscribe, not a second follower.
        entity.HasIndex(s => s.Endpoint).IsUnique();
    }
}
