using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FootballFormation.Core.Data.Configurations;

internal sealed class GameCommentConfiguration : IEntityTypeConfiguration<GameComment>
{
    public void Configure(EntityTypeBuilder<GameComment> entity)
    {
        entity.HasKey(c => c.Id);

        entity.Property(c => c.Body).IsRequired().HasMaxLength(2000);

        // SetNull, like GameGoal.Scorer: a comment is part of the match record and must survive the
        // account that wrote it. The author's name is only ever shown to admins anyway.
        entity.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);

        // Every read is "this game's comments, newest first".
        entity.HasIndex(c => new { c.GameId, c.CreatedAt });
    }
}
