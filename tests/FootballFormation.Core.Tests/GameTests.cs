using System.Globalization;

namespace FootballFormation.Core.Tests;

public class GameTests
{
    [Theory]
    [InlineData(GameSplitType.Halves, 60, 2, 30.0)]
    [InlineData(GameSplitType.Quarters, 60, 4, 15.0)]
    // A duration that does not divide into whole minutes keeps its fraction rather than truncating.
    [InlineData(GameSplitType.Halves, 45, 2, 22.5)]
    [InlineData(GameSplitType.Quarters, 50, 4, 12.5)]
    [InlineData(GameSplitType.Quarters, 45, 4, 11.25)]
    public void Period_count_and_length_follow_the_split(
        GameSplitType split, int duration, int expectedCount, double expectedLength)
    {
        var game = TestData.Game(split: split, durationMinutes: duration);

        Assert.Equal(expectedCount, game.PeriodCount);
        Assert.Equal((decimal)expectedLength, game.PeriodDurationMinutes);
    }

    [Theory]
    [InlineData(GameSplitType.Halves, 45)]
    [InlineData(GameSplitType.Quarters, 50)]
    [InlineData(GameSplitType.Quarters, 45)]
    [InlineData(GameSplitType.Halves, 61)]
    public void The_periods_always_add_back_up_to_the_full_match_length(
        GameSplitType split, int duration)
    {
        // Why period length is carried in seconds: the truncated fraction used to make four quarters of a 50-minute match add up to 48.
        var game = TestData.Game(split: split, durationMinutes: duration);

        Assert.Equal(duration * 60, game.PeriodCount * game.PeriodDurationSeconds);
    }

    [Fact]
    public void HasLineup_is_false_until_someone_is_placed()
    {
        var game = TestData.Game();
        game.AddPeriod(PeriodType.FirstHalf);

        Assert.False(game.HasLineup);

        game.Periods[0].PlayerPositions.Add(TestData.Starter(1, PlayerPosition.GK, 0));
        Assert.True(game.HasLineup);
    }

    [Fact]
    public void A_match_in_progress_is_never_complete_however_many_goals_are_logged()
    {
        var game = TestData.Game();
        game.MatchState = MatchState.InProgress;
        game.ScoreHome = 3;
        game.ScoreAway = 1;

        // Otherwise the season table would shift while the game is still being played.
        Assert.False(game.IsComplete);
    }

    [Theory]
    [InlineData(MatchState.Finished, null, null, true)]      // whistled off, score not yet typed in
    [InlineData(MatchState.Finished, 2, 1, true)]
    [InlineData(MatchState.NotStarted, 2, 1, true)]          // never run live, result entered by hand
    [InlineData(MatchState.NotStarted, null, null, false)]   // a future fixture
    [InlineData(MatchState.NotStarted, 2, null, false)]      // half-entered score
    public void IsComplete_covers_both_the_live_and_the_hand_entered_route(
        MatchState state, int? home, int? away, bool expected)
    {
        var game = TestData.Game();
        game.MatchState = state;
        game.ScoreHome = home;
        game.ScoreAway = away;

        Assert.Equal(expected, game.IsComplete);
    }

    [Fact]
    public void PlayedDurationMinutes_uses_real_timings_when_the_game_was_run_live()
    {
        var game = TestData.Game(durationMinutes: 60);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;      // 30 min
        second.StartedAtSeconds = 1800;
        second.EndedAtSeconds = 3300;     // 25 min — the ref cut it short

        Assert.True(game.HasActualTimings);
        Assert.Equal(55, game.PlayedDurationMinutes);
    }

    [Fact]
    public void PlayedDurationMinutes_falls_back_to_the_schedule_when_nothing_was_timed()
    {
        var game = TestData.Game(durationMinutes: 60);
        game.AddPeriod(PeriodType.FirstHalf);

        Assert.False(game.HasActualTimings);
        Assert.Equal(60, game.PlayedDurationMinutes);
    }

    [Fact]
    public void PlayedDurationMinutes_ignores_a_period_that_is_still_running()
    {
        var game = TestData.Game();
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;
        second.StartedAtSeconds = 1800;   // no end yet

        Assert.Equal(30, game.PlayedDurationMinutes);
    }

    /// A 60-minute match played out in two 30-minute halves, run live.
    private static Game TimedGame()
    {
        var game = TestData.Game(durationMinutes: 60);
        var first = game.AddPeriod(PeriodType.FirstHalf);
        var second = game.AddPeriod(PeriodType.SecondHalf);

        first.StartedAtSeconds = 0;
        first.EndedAtSeconds = 1800;
        second.StartedAtSeconds = 1800;
        second.EndedAtSeconds = 3600;
        return game;
    }

    [Fact]
    public void AvailableMinutesFor_stops_at_the_injury_and_gives_everyone_else_the_whole_match()
    {
        var game = TimedGame();
        TestData.Injury(game, game.Periods[1], playerId: 1, atSeconds: 2400, position: PlayerPosition.CM);

        Assert.Equal(40, game.AvailableMinutesFor(1));
        Assert.Equal(60, game.AvailableMinutesFor(2));
    }

    [Fact]
    public void AvailableMinutesFor_never_exceeds_what_the_match_actually_ran()
    {
        // Unreachable through the live screen, but it is what utilisation divides by, and over 100% reads as a broken report.
        var game = TimedGame();
        TestData.Injury(game, game.Periods[1], playerId: 1, atSeconds: 9999, position: PlayerPosition.CM);

        Assert.Equal(60, game.AvailableMinutesFor(1));
    }

    [Fact]
    public void AvailableMinutesFor_falls_back_to_the_schedule_on_a_game_never_run_live()
    {
        var game = TestData.Game(durationMinutes: 60);
        game.AddPeriod(PeriodType.FirstHalf);

        Assert.Equal(60, game.AvailableMinutesFor(1));
    }

    [Fact]
    public void An_injury_knows_whether_anybody_came_on_for_it()
    {
        var game = TimedGame();
        var replaced = TestData.Injury(game, game.Periods[0], playerId: 1, atSeconds: 600, position: PlayerPosition.CM);
        var alone = TestData.Injury(game, game.Periods[0], playerId: 2, atSeconds: 900, position: PlayerPosition.ST);

        var sub = TestData.Substitution(
            game, game.Periods[0], offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.CM);

        Assert.True(game.WasReplaced(replaced));
        Assert.False(game.WasReplaced(alone));
        Assert.Same(replaced, game.InjuryFor(sub));
    }

    [Fact]
    public void A_substitution_made_earlier_in_the_half_is_not_an_injurys_replacement()
    {
        // Off on 10', back on on 20', hurt on 30'. Pairing on the player alone would read the first substitution as the replacement.
        var game = TimedGame();
        var half = game.Periods[0];
        TestData.Substitution(game, half, offId: 1, onId: 2, atSeconds: 600, position: PlayerPosition.CM);
        TestData.Substitution(game, half, offId: 2, onId: 1, atSeconds: 1200, position: PlayerPosition.CM);
        var injury = TestData.Injury(game, half, playerId: 1, atSeconds: 1500, position: PlayerPosition.CM);

        Assert.False(game.WasReplaced(injury));
        Assert.Null(game.InjuryFor(game.Substitutions[0]));
    }

    [Fact]
    public void A_substitution_nobody_was_hurt_for_has_no_injury_to_find()
    {
        var game = TimedGame();
        var sub = TestData.Substitution(
            game, game.Periods[0], offId: 1, onId: 3, atSeconds: 600, position: PlayerPosition.CM);

        Assert.Null(game.InjuryFor(sub));
    }

    [Fact]
    public void ElapsedSecondsAt_adds_the_running_stretch_to_the_banked_total()
    {
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 600;
        game.ClockRunningSince = now.AddSeconds(-45);

        Assert.True(game.IsClockRunning);
        Assert.Equal(645, game.ElapsedSecondsAt(now));
    }

    [Fact]
    public void A_stopped_clock_reads_the_same_at_any_instant()
    {
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 600;
        game.ClockRunningSince = null;

        Assert.False(game.IsClockRunning);
        Assert.Equal(600, game.ElapsedSecondsAt(DateTime.UtcNow));
        Assert.Equal(600, game.ElapsedSecondsAt(DateTime.UtcNow.AddYears(5)));
    }

    [Fact]
    public void A_clock_anchor_in_the_future_never_runs_backwards()
    {
        // Clock skew must not produce a negative elapsed time, which would read as a match un-playing itself.
        var now = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var game = TestData.Game();
        game.ClockAccumulatedSeconds = 100;
        game.ClockRunningSince = now.AddSeconds(30);

        Assert.Equal(100, game.ElapsedSecondsAt(now));
    }

    [Fact]
    public void Squad_players_are_in_the_roster_unless_marked_unavailable()
    {
        var player = TestData.Player(1);
        var squad = TestData.Squad(1, [player]);
        var game = TestData.Game();

        Assert.True(game.IsInRoster(player, squad));

        game.UnavailablePlayerIds.Add(player.Id);
        Assert.False(game.IsInRoster(player, squad));
    }

    [Fact]
    public void Guests_are_out_of_the_roster_unless_explicitly_added()
    {
        var guest = TestData.Player(2);
        var squad = TestData.Squad(1, [guest], guestIds: [2]);
        var game = TestData.Game();

        Assert.False(game.IsInRoster(guest, squad));

        game.GuestPlayerIds.Add(guest.Id);
        Assert.True(game.IsInRoster(guest, squad));
    }

    [Fact]
    public void IsInRoster_is_blind_to_the_squads_injury_flag_the_way_it_is_blind_to_archiving()
    {
        // The squad flag is live and undated, so letting it reach in here would zero out AvailableMinutes for every game already played.
        var player = TestData.Player(1);
        var squad = TestData.Squad(1, [player], injuredIds: [1]);
        var game = TestData.Game();

        Assert.True(game.IsInRoster(player, squad));
    }

    [Fact]
    public void A_game_that_recorded_her_injured_takes_her_out_of_its_own_roster()
    {
        // The other half of that: what the game wrote down at the final whistle does count, being history rather than a status.
        var player = TestData.Player(1);
        var squad = TestData.Squad(1, [player]);
        var game = TestData.Game();

        game.InjuredPlayerIds.Add(player.Id);
        Assert.False(game.IsInRoster(player, squad));
    }

    [Fact]
    public void An_injured_guest_is_still_judged_by_whether_they_were_explicitly_added()
    {
        var guest = TestData.Player(2);
        var squad = TestData.Squad(1, [guest], guestIds: [2], injuredIds: [2]);
        var game = TestData.Game();
        game.GuestPlayerIds.Add(guest.Id);

        Assert.True(game.IsInRoster(guest, squad));
    }

    [Fact]
    public void The_roster_rule_reads_each_games_own_season()
    {
        // Guest in season 1, regular in season 2 — the same player, judged differently per game.
        var player = TestData.Player(1);
        var squads = new SeasonSquads([
            new SeasonSquadMember { SeasonId = 1, PlayerId = 1, Player = player, IsGuest = true },
            new SeasonSquadMember { SeasonId = 2, PlayerId = 1, Player = player, IsGuest = false }
        ]);

        var guestSeasonGame = TestData.Game(id: 1, seasonId: 1);
        var memberSeasonGame = TestData.Game(id: 2, seasonId: 2);

        Assert.False(guestSeasonGame.IsInRoster(player, squads));
        Assert.True(memberSeasonGame.IsInRoster(player, squads));
    }

    [Fact]
    public void SelectRoster_keeps_only_the_players_taking_part()
    {
        var a = TestData.Player(1, "A");
        var b = TestData.Player(2, "B");
        var guest = TestData.Player(3, "G");
        var squad = TestData.Squad(1, [a, b, guest], guestIds: [3]);

        var game = TestData.Game();
        game.UnavailablePlayerIds.Add(b.Id);
        game.GuestPlayerIds.Add(guest.Id);

        Assert.Equal([1, 3], game.SelectRoster([a, b, guest], squad).Select(p => p.Id));
    }

    [Fact]
    public void An_own_goal_counts_for_the_opponent_and_not_for_us()
    {
        List<GameGoal> goals = [
            TestData.Goal(scorerId: 1),
            TestData.Goal(scorerId: 2),
            TestData.Goal(scorerId: 3, ownGoal: true),
            TestData.Goal(opponentGoal: true)
        ];

        Assert.Equal(2, Game.CountOurGoals(goals));
        Assert.Equal(2, Game.CountTheirGoals(goals));
    }

    [Fact]
    public void The_clock_goes_from_one_half_to_the_next_rather_than_from_quarter_to_quarter()
    {
        var game = QuartersGame();

        // Before kick-off the next period is simply the first one.
        Assert.Equal(PeriodType.FirstQuarter, game.NextHalf()!.PeriodType);

        // The first half has been played, so the line-up planned for the rest of it is behind the clock.
        game.Periods.Single(p => p.PeriodType == PeriodType.FirstQuarter).StartedAtSeconds = 0;
        Assert.Equal(PeriodType.ThirdQuarter, game.NextHalf()!.PeriodType);

        game.Periods.Single(p => p.PeriodType == PeriodType.ThirdQuarter).StartedAtSeconds = 1800;
        Assert.Null(game.NextHalf());
    }

    /// Only a quarters game has one, and it is looked up from the plan rather than the clock — so it reads the same before kick-off as
    /// during play.
    [Fact]
    public void Only_a_half_planned_in_two_line_ups_has_a_plan_for_its_middle()
    {
        var quarters = QuartersGame();
        var firstHalf = quarters.Periods.Single(p => p.PeriodType == PeriodType.FirstQuarter);
        var secondHalf = quarters.Periods.Single(p => p.PeriodType == PeriodType.ThirdQuarter);

        Assert.Equal(PeriodType.SecondQuarter, quarters.MidHalfPlan(firstHalf)!.PeriodType);
        Assert.Equal(PeriodType.FourthQuarter, quarters.MidHalfPlan(secondHalf)!.PeriodType);

        // The plan itself has nothing planned after it — the half ends there.
        Assert.Null(quarters.MidHalfPlan(quarters.MidHalfPlan(firstHalf)!));

        var halves = TestData.Game();
        halves.AddPeriod(PeriodType.FirstHalf);
        halves.AddPeriod(PeriodType.SecondHalf);

        // The second half is not a change due inside the first — it is the next half.
        Assert.Null(halves.MidHalfPlan(halves.Periods[0]));
    }

    private static Game QuartersGame() => new()
    {
        Opponent = "X",
        SplitType = GameSplitType.Quarters,
        Periods = [.. PeriodTypeExtensions.ForSplitType(GameSplitType.Quarters)
            .Select(type => new GamePeriod { PeriodType = type })]
    };

    [Fact]
    public void Split_period_count_is_derived_from_the_period_table_itself()
    {
        foreach (var split in Enum.GetValues<GameSplitType>())
            Assert.Equal(PeriodTypeExtensions.ForSplitType(split).Length, split.PeriodCount());
    }

    [Theory]
    [InlineData(true, 3, 1, 3, 1)]
    [InlineData(false, 3, 1, 1, 3)]
    public void ScoreboardOrder_puts_the_home_side_first(
        bool isHomeGame, int ours, int theirs, int expectedHome, int expectedAway)
    {
        var game = TestData.Game();
        game.IsHomeGame = isHomeGame;
        game.ScoreHome = ours;
        game.ScoreAway = theirs;

        Assert.Equal(new VenueScore(expectedHome, expectedAway), game.ScoreboardOrder());
    }

    [Fact]
    public void ScoreboardOrder_reads_a_score_not_yet_typed_in_as_nil_all()
    {
        Assert.Equal(new VenueScore(0, 0), TestData.Game().ScoreboardOrder());
    }

    [Fact]
    public void A_venue_score_reads_home_side_first_with_an_en_dash()
    {
        Assert.Equal("3 – 1", new VenueScore(3, 1).ToString());
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(14, 30, 0, true)]
    public void HasStartTime_is_false_until_a_kick_off_time_is_set(int hour, int minute, int second, bool expected)
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, hour, minute, second));

        Assert.Equal(expected, game.HasStartTime);
    }

    /// The month name comes from the ambient culture, so an assertion in English only holds where the machine happens to be English —
    /// these read green on the Linux runner and red on a Dutch Windows box until the culture is the test's rather than the machine's.
    private static string DateLine(Game game, string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return game.DateLine("d MMMM yyyy");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DateLine_drops_the_kick_off_time_when_none_was_set()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14));

        Assert.Equal("14 March 2026", DateLine(game, "en-GB"));
    }

    [Fact]
    public void DateLine_appends_the_kick_off_time_when_one_was_set()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 19, 30, 0));

        Assert.Equal("14 March 2026, 19:30", DateLine(game, "en-GB"));
    }

    [Fact]
    public void DateLine_writes_the_month_in_the_language_the_page_is_being_read_in()
    {
        var game = TestData.Game(date: new DateTime(2026, 3, 14, 19, 30, 0));

        // Dutch is the site's default, so following the culture is the behaviour — not something to format away with InvariantCulture.
        Assert.Equal("14 maart 2026, 19:30", DateLine(game, "nl-NL"));
    }

    [Theory]
    [InlineData(MatchState.Finished, 2, 1, true)]
    [InlineData(MatchState.NotStarted, 2, 1, true)]
    [InlineData(MatchState.NotStarted, null, null, false)]
    [InlineData(MatchState.InProgress, 2, 1, false)]  // a score written mid-match is not settled yet
    public void HasFinalScore_checks_the_match_state_as_well_as_the_scores(
        MatchState state, int? home, int? away, bool expected)
    {
        var game = TestData.Game();
        game.MatchState = state;
        game.ScoreHome = home;
        game.ScoreAway = away;

        Assert.Equal(expected, game.HasFinalScore);
    }
}
