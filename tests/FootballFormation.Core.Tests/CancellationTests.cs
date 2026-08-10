using FootballFormation.Core.Data;
using FootballFormation.Core.Models;
using FootballFormation.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FootballFormation.Core.Tests;

/// <summary>
/// What happens when the caller goes away mid-call: an ordinary outcome, not an error.
/// </summary>
public class CancellationTests : ServiceTestBase
{
    [Fact]
    public async Task A_read_the_caller_has_already_given_up_on_comes_back_cancelled_not_failed()
    {
        await SeedPlayersAsync(3);

        var result = await Players.GetAllAsync(new CancellationToken(canceled: true));

        Assert.True(result.IsCancelled);
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task A_cancelled_read_carries_no_message_because_there_is_nobody_left_to_show_one()
    {
        await SeedPlayersAsync(1);

        var result = await Players.GetAllAsync(new CancellationToken(canceled: true));

        // The UI keys its snackbar off ErrorKey, and would raise it on the page they moved to.
        Assert.Null(result.ErrorKey);
        Assert.Null(result.Error);
        Assert.NotEqual(ServiceOperation.UnexpectedFailureKey, result.ErrorKey);
    }

    [Fact]
    public async Task A_query_abandoned_after_it_started_is_cancelled_rather_than_an_unexpected_failure()
    {
        await SeedPlayersAsync(3);

        // Live when the service is entered and tripped once the query is under way, so this takes
        // the OperationCanceledException path rather than the before-we-start guard.
        using var cts = new CancellationTokenSource();
        var players = new PlayerService(
            new CancellingDbContextFactory(DbFactory, cts), CurrentUser, NullLogger<PlayerService>.Instance);

        var result = await players.GetAllAsync(cts.Token);

        Assert.True(result.IsCancelled);
        Assert.Null(result.ErrorKey);
    }

    [Fact]
    public async Task A_cancellation_nobody_asked_for_is_still_an_unexpected_failure()
    {
        // Not the caller leaving but something inside giving up. The catch filter tells them apart.
        var players = new PlayerService(
            new ThrowingDbContextFactory(new OperationCanceledException()),
            CurrentUser,
            NullLogger<PlayerService>.Instance);

        var result = await players.GetAllAsync(CancellationToken.None);

        Assert.False(result.IsCancelled);
        Assert.Equal(ServiceOperation.UnexpectedFailureKey, result.ErrorKey);
        Assert.Equal(["load players"], result.ErrorArgs);
    }

    [Fact]
    public async Task A_write_the_caller_gave_up_on_is_refused_before_anything_is_written()
    {
        var player = new Player { FirstName = "Nobody", PreferredPosition = PlayerPosition.CM };

        var result = await Players.CreateAsync(player, new CancellationToken(canceled: true));

        Assert.True(result.IsCancelled);

        await using var db = Read();
        Assert.Empty(db.Players);
    }

    [Fact]
    public async Task An_abandoned_write_is_never_reported_as_an_authorization_refusal()
    {
        // Someone who closed the tab attempted nothing, so the refusal log stays meaningful.
        CurrentUser.IsAdmin = false;

        var result = await Players.CreateAsync(
            new Player { FirstName = "Nobody", PreferredPosition = PlayerPosition.CM },
            new CancellationToken(canceled: true));

        Assert.True(result.IsCancelled);
        Assert.NotEqual(ServiceOperation.NotAllowedKey, result.ErrorKey);
    }

    [Fact]
    public async Task A_call_given_no_token_runs_to_completion_as_it_always_did()
    {
        await SeedPlayersAsync(2);

        // Every token parameter defaults, so existing call sites behave exactly as before.
        var result = await Players.GetAllAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task An_abandoned_load_of_a_missing_game_reads_as_cancelled_rather_than_not_found()
    {
        // The distinction the pages lean on: "not found" redirects, and cancelled must not.
        var result = await Games.GetByIdAsync(404, new CancellationToken(canceled: true));

        Assert.True(result.IsCancelled);
        Assert.Null(result.ErrorKey);
    }

    /// <summary>Trips the token after handing out a context, so the caller leaves mid-operation.</summary>
    private sealed class CancellingDbContextFactory(
        IDbContextFactory<AppDbContext> inner, CancellationTokenSource cancellation)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => inner.CreateDbContext();

        public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var db = await inner.CreateDbContextAsync(cancellationToken);
            await cancellation.CancelAsync();
            return db;
        }
    }

    /// <summary>Fails the way the thing under test needs it to, before any database is involved.</summary>
    private sealed class ThrowingDbContextFactory(Exception failure) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw failure;

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<AppDbContext>(failure);
    }
}
