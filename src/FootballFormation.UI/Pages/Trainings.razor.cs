using System.Globalization;

namespace FootballFormation.UI.Pages;

/// Admin-only, unlike the squad and the fixtures: an absence is a personal fact, and the reason for it is usually in the note beside it.
public partial class Trainings
{
    [Inject] private TrainingService TrainingService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private TimeProvider Time { get; set; } = null!;
    [Inject] private IStringLocalizer<Strings> L { get; set; } = null!;

    private List<Training>? _trainings;

    protected override async Task LoadAsync()
    {
        var result = await TrainingService.GetAllAsync(SeasonId, Cancellation);
        _trainings = Snackbar.ReportFailure(L, result) ? result.Value : [];
    }

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
