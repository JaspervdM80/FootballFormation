using FootballFormation.Core.Models;

namespace FootballFormation.Core.Tests;

public class SeasonSquadTests
{
    private static readonly Player Regular = TestData.Player(1, "Regular", shirt: 7);
    private static readonly Player Guest = TestData.Player(2, "Guest", shirt: 3);

    [Fact]
    public void A_player_outside_the_squad_counts_as_a_guest_not_a_member()
    {
        var squad = TestData.Squad(1, [Regular]);

        // Both "guest" and "unknown" mean not-a-regular, which is the only distinction the roster
        // rule needs — and it keeps games referencing a departed player rendering sensibly.
        Assert.True(squad.IsGuest(999));
        Assert.False(squad.IsFullMember(999));
        Assert.False(squad.Contains(999));
    }

    [Fact]
    public void Guests_sort_last_then_by_shirt_number_with_unnumbered_players_after()
    {
        var noShirt = TestData.Player(3, "Anna");
        var squad = TestData.Squad(1, [Guest, noShirt, Regular], guestIds: [2]);

        // Regular (#7) → Anna (no number, sorts as int.MaxValue) → Guest (#3, but a guest).
        Assert.Equal([1, 3, 2], squad.Members.Select(m => m.PlayerId));
        Assert.Equal([1, 3], squad.FullMembers.Select(p => p.Id));
        Assert.Equal([2], squad.Guests.Select(p => p.Id));
    }

    [Fact]
    public void Empty_squad_treats_everyone_as_a_guest()
    {
        Assert.True(SeasonSquad.Empty.IsGuest(1));
        Assert.False(SeasonSquad.Empty.IsFullMember(1));
        Assert.Empty(SeasonSquad.Empty.Members);
    }

    [Fact]
    public void A_player_outside_the_squad_is_never_injured()
    {
        var squad = TestData.Squad(1, [Regular]);

        Assert.False(squad.IsInjured(999));
        Assert.False(SeasonSquad.Empty.IsInjured(1));
    }

    [Fact]
    public void Injured_members_are_reported_by_IsInjured_and_the_Injured_list()
    {
        var squad = TestData.Squad(1, [Regular, Guest], injuredIds: [Regular.Id]);

        Assert.True(squad.IsInjured(Regular.Id));
        Assert.False(squad.IsInjured(Guest.Id));
        Assert.Equal([Regular.Id], squad.Injured.Select(p => p.Id));
    }

    [Fact]
    public void SeasonSquads_judges_each_season_separately()
    {
        // The case the plural type exists for: a guest in 2024/25, a regular in 2025/26.
        var squads = new SeasonSquads([
            new SeasonSquadMember { SeasonId = 1, PlayerId = 1, Player = Regular, IsGuest = true },
            new SeasonSquadMember { SeasonId = 2, PlayerId = 1, Player = Regular, IsGuest = false }
        ]);

        Assert.True(squads.For(1).IsGuest(1));
        Assert.True(squads.For(2).IsFullMember(1));
        Assert.True(squads.IsFullMemberAnywhere(1));
    }

    [Fact]
    public void An_unknown_season_yields_an_empty_squad_rather_than_throwing()
    {
        var squads = SeasonSquads.Of(TestData.Squad(1, [Regular]));

        Assert.Empty(squads.For(42).Members);
        Assert.Same(SeasonSquad.Empty, squads.For(42));
    }

    [Fact]
    public void AllPlayers_deduplicates_across_seasons()
    {
        var squads = new SeasonSquads([
            new SeasonSquadMember { SeasonId = 1, PlayerId = 1, Player = Regular },
            new SeasonSquadMember { SeasonId = 2, PlayerId = 1, Player = Regular },
            new SeasonSquadMember { SeasonId = 2, PlayerId = 2, Player = Guest }
        ]);

        Assert.Equal([1, 2], squads.AllPlayers.Select(p => p.Id).Order());
    }
}
