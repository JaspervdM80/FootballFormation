using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Components;

public record PitchSwapRequest(PeriodType Half, int AtSeconds, int PlayerAId, int PlayerBId);

public record PitchSubstitutionRequest(PeriodType Half, int AtSeconds, int PlayerOffId, int PlayerOnId);

/// One played half on a pitch, stepped a minute at a time. Step 0 is the kick-off itself; step n is the end of the half's nth minute, so
/// everything the timeline files under a minute is already on the pitch when that minute is shown — and a change made there lands in it.
public partial class LineupByMinute
{
    [Parameter, EditorRequired]
    public Game Game { get; set; } = null!;

    [Parameter, EditorRequired]
    public Func<int, Player?> FindPlayer { get; set; } = _ => null;

    /// Closes off a half still being played.
    [Parameter]
    public int ElapsedSeconds { get; set; }

    [Parameter]
    public bool CanEdit { get; set; }

    [Parameter]
    public EventCallback<PitchSwapRequest> OnSwap { get; set; }

    [Parameter]
    public EventCallback<PitchSubstitutionRequest> OnSubstitute { get; set; }

    private PeriodType _half = PeriodType.FirstHalf;
    private int _step;
    private int? _selectedId;

    private Game? _walkedGame;
    private PeriodType _walkedHalf;
    private HalfWalkResult? _walk;

    private List<PeriodType> Halves =>
        [.. new[] { PeriodType.FirstHalf, PeriodType.SecondHalf }.Where(h => Game.PlayedHalf(h) is not null)];

    private GamePeriod? Period => Game.PlayedHalf(_half);

    private int Start => Period?.StartedAtSeconds ?? 0;

    private int End => Math.Max(Start, Period?.EndedAtSeconds ?? ElapsedSeconds);

    private int MaxStep => Math.Max(1, (int)Math.Ceiling((End - Start) / 60.0));

    private int AtSeconds => _step == 0 ? Start : Math.Min(Start + (_step * 60) - 1, End);

    private FormationType Formation => Period?.FormationTypeOverride ?? Game.FormationType;

    protected override void OnParametersSet()
    {
        if (!Halves.Contains(_half) && Halves.Count > 0) _half = Halves[0];
        _step = Math.Clamp(_step, 0, MaxStep);
    }

    /// Walked once per half per load of the game rather than per render: the slider re-renders on every step.
    private HalfWalkResult? Walk
    {
        get
        {
            if (Period is not { StartedAtSeconds: not null } period) return null;
            if (_walk is null || !ReferenceEquals(_walkedGame, Game) || _walkedHalf != _half)
            {
                _walk = HalfLineupWalk.Rewind(Game, period).Walk(Start, End);
                (_walkedGame, _walkedHalf) = (Game, _half);
            }
            return _walk;
        }
    }

    private IReadOnlyDictionary<int, PitchSpot> OnPitchNow =>
        Walk is { } walk ? HalfLineupWalk.At(walk, AtSeconds).OnPitch : new Dictionary<int, PitchSpot>();

    private List<GamePlayerPosition> PitchPositions =>
    [
        .. OnPitchNow
            .Select(kv => (Spot: kv.Value, Player: FindPlayer(kv.Key)))
            .Where(entry => entry.Player is not null)
            .Select(entry => new GamePlayerPosition
            {
                PlayerId = entry.Player!.Id,
                Player = entry.Player,
                SlotIndex = entry.Spot.SlotIndex,
                Position = entry.Spot.Position
            })
    ];

    /// The half's own line-up off the pitch at this moment, less anyone the match has already lost to an injury — she cannot come back on.
    private List<Player> Bench
    {
        get
        {
            if (Period is null) return [];

            var onPitch = OnPitchNow;
            var hurt = Game.Injuries.Where(i => i.AtSeconds <= AtSeconds).Select(i => i.PlayerId).ToHashSet();

            return
            [
                .. Period.PlayerPositions
                    .Where(pp => !onPitch.ContainsKey(pp.PlayerId) && !hurt.Contains(pp.PlayerId))
                    .Select(pp => FindPlayer(pp.PlayerId))
                    .OfType<Player>()
                    .OrderBy(p => p.ShirtNumber ?? int.MaxValue)
                    .ThenBy(p => p.DisplayName)
            ];
        }
    }

    /// The half's substitutions, swaps and injuries, the way the timeline files them.
    private List<MatchEvent> Changes =>
    [
        .. MatchTimelineReport.Build(Game, includeSubstitutions: true, newestFirst: false)
            .Where(e => e.Goal is null && e.Half == _half)
    ];

    private string StepLabel => _step == 0
        ? L["Start of half"].Value
        : $"{MatchClockReport.Build(Game, Period, AtSeconds).Minute}'";

    private int? SelectedSlot =>
        _selectedId is { } id && OnPitchNow.TryGetValue(id, out var spot) ? spot.SlotIndex : null;

    private bool BenchSelected => _selectedId is { } id && !OnPitchNow.ContainsKey(id);

    private void ChooseHalf(PeriodType half)
    {
        _half = half;
        _step = 0;
        _selectedId = null;
    }

    private void StepTo(int step)
    {
        _step = Math.Clamp(step, 0, MaxStep);
        _selectedId = null;
    }

    private void OnSliderInput(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var step)) StepTo(step);
    }

    /// The step a change first shows at: its own minute, or the kick-off for one made at the whistle that started the half.
    private void JumpTo(MatchEvent change) =>
        StepTo(change.AtSeconds <= Start ? 0 : (int)Math.Ceiling((change.AtSeconds - Start + 1) / 60.0));

    private async Task OnPitchPlayerClicked(int playerId)
    {
        if (!CanEdit) return;

        if (_selectedId is not { } selected)
        {
            _selectedId = playerId;
            return;
        }

        if (selected == playerId)
        {
            _selectedId = null;
            return;
        }

        _selectedId = null;

        if (OnPitchNow.ContainsKey(selected))
            await OnSwap.InvokeAsync(new PitchSwapRequest(_half, AtSeconds, selected, playerId));
        else
            await OnSubstitute.InvokeAsync(new PitchSubstitutionRequest(_half, AtSeconds, playerId, selected));
    }

    private async Task OnBenchPlayerClicked(int playerId)
    {
        if (!CanEdit) return;

        if (_selectedId is { } selected && OnPitchNow.ContainsKey(selected))
        {
            _selectedId = null;
            await OnSubstitute.InvokeAsync(new PitchSubstitutionRequest(_half, AtSeconds, selected, playerId));
            return;
        }

        _selectedId = _selectedId == playerId ? null : playerId;
    }
}
