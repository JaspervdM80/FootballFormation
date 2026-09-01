namespace FootballFormation.Core.Tests;

/// The one rule that matters: a private comment must not leave the service for a caller who is not an admin. The visibility filter is
/// all that stands between a coach's working notes and a public club site.
public class GameCommentTests : ServiceTestBase
{
    private Season? _season;

    /// One season for the whole test — StartDate is unique, so seeding a second would fail.
    private async Task<Game> SeedGameAsync()
    {
        _season ??= await SeedSeasonAsync();

        var game = await Games.CreateAsync(new Game
        {
            Opponent = "Opponent",
            Date = Now,
            SeasonId = _season.Id
        });

        return game.Value!;
    }

    [Fact]
    public async Task A_new_comment_is_private_unless_it_says_otherwise()
    {
        var game = await SeedGameAsync();

        var added = await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Keep this in-house" });

        Assert.True(added.IsSuccess);
        Assert.False(added.Value!.IsPublic);
        Assert.Null(added.Value.EditedAt);
    }

    [Fact]
    public async Task A_visitor_is_given_only_the_published_comments()
    {
        var game = await SeedGameAsync();
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Private note" });
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Match report", IsPublic = true });

        var asVisitor = await Games.GetCommentsAsync(game.Id, includePrivate: false);
        var asAdmin = await Games.GetCommentsAsync(game.Id, includePrivate: true);

        // The body, not just the count: the page prerenders server-side, so a private body reaching
        // the caller at all is the leak — whether or not the markup goes on to render it.
        Assert.Equal(["Match report"], asVisitor.Value!.Select(c => c.Body));
        Assert.Equal(2, asAdmin.Value!.Count);
    }

    [Fact]
    public async Task Comments_belong_to_their_own_game()
    {
        var game = await SeedGameAsync();
        var other = await SeedGameAsync();
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "About this game" });

        var forOther = await Games.GetCommentsAsync(other.Id, includePrivate: true);

        Assert.Empty(forOther.Value!);
    }

    [Fact]
    public async Task Publishing_a_comment_does_not_mark_it_as_edited()
    {
        var game = await SeedGameAsync();
        var added = await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Match report" });

        var published = await Games.UpdateCommentAsync(added.Value!.Id, "Match report", isPublic: true);

        Assert.True(published.IsSuccess);
        await using var db = Read();
        var stored = await db.GameComments.FirstAsync(c => c.Id == added.Value.Id);
        Assert.True(stored.IsPublic);
        Assert.Null(stored.EditedAt);
    }

    [Fact]
    public async Task Changing_the_body_marks_the_comment_as_edited()
    {
        var game = await SeedGameAsync();
        var added = await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "First take" });

        await Games.UpdateCommentAsync(added.Value!.Id, "Second take", isPublic: false);

        await using var db = Read();
        var stored = await db.GameComments.FirstAsync(c => c.Id == added.Value.Id);
        Assert.Equal("Second take", stored.Body);
        Assert.NotNull(stored.EditedAt);
    }

    [Fact]
    public async Task An_unpublished_comment_is_hidden_from_visitors_again()
    {
        var game = await SeedGameAsync();
        var added = await Games.AddCommentAsync(
            new GameComment { GameId = game.Id, Body = "Published too soon", IsPublic = true });

        await Games.UpdateCommentAsync(added.Value!.Id, added.Value.Body, isPublic: false);

        var asVisitor = await Games.GetCommentsAsync(game.Id, includePrivate: false);
        Assert.Empty(asVisitor.Value!);
    }

    [Fact]
    public async Task A_removed_comment_is_gone()
    {
        var game = await SeedGameAsync();
        var added = await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Never mind" });

        var removed = await Games.RemoveCommentAsync(added.Value!.Id);

        Assert.True(removed.IsSuccess);
        Assert.Empty((await Games.GetCommentsAsync(game.Id, includePrivate: true)).Value!);
    }

    [Fact]
    public async Task Updating_or_removing_a_comment_that_is_not_there_fails_rather_than_throwing()
    {
        Assert.True((await Games.UpdateCommentAsync(999, "ghost", isPublic: true)).IsFailure);
        Assert.True((await Games.RemoveCommentAsync(999)).IsFailure);
    }

    [Fact]
    public async Task Deleting_a_game_takes_its_comments_with_it()
    {
        var game = await SeedGameAsync();
        await Games.AddCommentAsync(new GameComment { GameId = game.Id, Body = "Goes with the game" });

        await Games.DeleteAsync(game.Id);

        await using var db = Read();
        Assert.Empty(await db.GameComments.ToListAsync());
    }

    [Fact]
    public async Task A_comment_outlives_the_account_that_wrote_it()
    {
        SeedTeam();
        var game = await SeedGameAsync();
        var author = await Users.CreateAsync("Jasper", "jasper", "correct-horse", UserRole.Admin);
        // A second admin, so deleting the first is not blocked by the last-admin rule.
        await Users.CreateAsync("Someone Else", "other", "correct-horse", UserRole.Admin);

        var added = await Games.AddCommentAsync(new GameComment
        {
            GameId = game.Id,
            Body = "Written by someone who later left",
            AuthorId = author.Value!.Id
        });
        Assert.Equal(author.Value.DisplayName, added.Value!.Author?.DisplayName);

        await Users.DeleteAsync(author.Value.Id);

        var comments = await Games.GetCommentsAsync(game.Id, includePrivate: true);
        var survivor = Assert.Single(comments.Value!);
        Assert.Equal("Written by someone who later left", survivor.Body);
        Assert.Null(survivor.AuthorId);
    }
}
