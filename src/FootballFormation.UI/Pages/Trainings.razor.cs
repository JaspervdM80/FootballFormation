using System.Globalization;
using FootballFormation.Core.Reporting;

namespace FootballFormation.UI.Pages;

/// Admin-only, unlike the squad and the fixtures: an absence is a personal fact, and the reason for it is usually in the note beside it.
public partial class Trainings
{
    [Inject] private TrainingService TrainingService { get; set; } = null!;
    [Inject] private PlayerService PlayerService { get; set; } = null!;
    [Inject] private StatsService StatsService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private List<Training>? _trainings;
    private List<Player> _players = [];
    private TrainingAttendance? _attendance;

    protected override async Task LoadAsync()
    {
        var result = await TrainingService.GetAllAsync(SeasonId, Cancellation);
        _trainings = Snackbar.ReportFailure(L, result) ? result.Value : [];

        // A second read rather than the list already in hand: attendance is the squad minus the absentees, and the squad is not this
        // page's to load. Every write here reloads, so the figure never lags the register.
        var attendance = await StatsService.GetTrainingAttendanceAsync(SeasonId, Cancellation);
        _attendance = Snackbar.ReportFailure(L, attendance) ? attendance.Value : TrainingAttendance.Empty;

        // The whole roster rather than the attendance rows: those are one season's full members with sessions already behind them, and
        // a badge can sit on an evening still to come, on "All seasons", or on a player since archived.
        var players = await PlayerService.GetAllAsync(Cancellation);
        _players = Snackbar.ReportFailure(L, players) ? players.Value! : [];
    }

    /// In the order PlayerService hands them over — shirt number, then name — so the same absences read the same way on every session.
    private string UnavailableNames(Training training) =>
        string.Join(", ", _players
            .Where(player => training.UnavailablePlayerIds.Contains(player.Id))
            .Select(player => player.DisplayName));

    private sealed record TrainingWeek(string Title, bool OpensThePast, List<Training> Trainings);

    /// Grouped the way the team actually plans: by the week the session falls in, in the order TrainingService hands them over — this
    /// week and the weeks ahead first, the weeks already over below them. ISO weeks, because a week here runs Monday to Sunday.
    private List<TrainingWeek> Weeks()
    {
        if (_trainings is null) return [];

        var thisMonday = TrainingOrdering.MondayOf(Time.GetLocalNow().Date);
        var weeks = _trainings
            .GroupBy(t => TrainingOrdering.MondayOf(t.Date))
            .Select(week => (Monday: week.Key, Trainings: week.ToList()))
            .ToList();

        // No past week leaves this at MinValue, which no Monday matches — so the divider simply never renders.
        var firstPast = weeks.FirstOrDefault(week => week.Monday < thisMonday).Monday;

        return [.. weeks.Select(week => new TrainingWeek(
            $"{L["Week {0}", ISOWeek.GetWeekOfYear(week.Monday)]} · {week.Monday:dd MMM} – {week.Monday.AddDays(6):dd MMM}",
            week.Monday == firstPast,
            week.Trainings))];
    }

    private async Task OpenAddDialog()
    {
        var training = await ShowTrainingDialogAsync(L["New Training"]);
        if (training is null) return;

        var result = await TrainingService.CreateAsync(training);
        Snackbar.Report(L, result, L["Training on {0} added", training.Date.ToString("dd MMM")]);
        await LoadAsync();
    }

    private async Task OpenEditDialog(Training training)
    {
        var updated = await ShowTrainingDialogAsync(L["Edit Training"], training);
        if (updated is null) return;

        var result = await TrainingService.UpdateAsync(updated);
        Snackbar.Report(L, result, L["Training on {0} updated", updated.Date.ToString("dd MMM")]);
        await LoadAsync();
    }

    private async Task DeleteTraining(Training training)
    {
        var confirmed = await DialogService.ConfirmDeleteAsync(
            L["Delete Training"],
            L["Delete the training on {0}, along with the absences recorded for it?", training.Date.ToString("dd MMM yyyy")]);
        if (!confirmed) return;

        var result = await TrainingService.DeleteAsync(training.Id);
        Snackbar.Report(L, result, L["Training on {0} deleted", training.Date.ToString("dd MMM")], Severity.Warning);
        await LoadAsync();
    }

    /// Null when the dialog was cancelled.
    private async Task<Training?> ShowTrainingDialogAsync(string title, Training? training = null) =>
        await DialogService.PromptAsync<TrainingDialog, Training>(title, p =>
        {
            if (training is not null) p.Add(x => x.Training, training);
        });
}
