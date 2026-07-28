using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class WorkflowSessionPageViewModel : ViewModelBase
{
    public WorkflowSessionPageViewModel()
    {
        ResetCommand = new AsyncRelayCommand(
            _ => ResetRequestedAsync?.Invoke() ?? Task.CompletedTask);
        AddViewerSampleCommand = new AsyncRelayCommand(
            _ => AddViewerSampleAsync());
    }

    public string Phase
    {
        get;
        private set => SetProperty(ref field, value);
    } = StreamPhase.Idle.ToString();

    public string Countdown
    {
        get;
        private set => SetProperty(ref field, value);
    } = "00:00";

    public string Scene
    {
        get;
        private set => SetProperty(ref field, value);
    } = "-";

    public string PeakViewers
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0";

    public string AverageViewers
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0.0";

    public string Followers
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0";

    public string ChatAlerts
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0 / 0";

    public string ViewerSample
    {
        get;
        set => SetProperty(ref field, value);
    } = "0";

    public AsyncRelayCommand ResetCommand { get; }

    public AsyncRelayCommand AddViewerSampleCommand { get; }

    public Func<Task>? ResetRequestedAsync { get; set; }

    public Func<int, Task>? AddViewerSampleRequestedAsync { get; set; }

    public void Update(
        WorkflowState state,
        StreamSessionStats stats)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stats);

        Phase = state.Phase.ToString();
        Countdown = TimeSpan.FromSeconds(
                Math.Max(0, state.CountdownRemainingSeconds))
            .ToString(@"mm\:ss");
        Scene = string.IsNullOrWhiteSpace(state.CurrentScene)
            ? "-"
            : state.CurrentScene;
        PeakViewers = stats.PeakViewers.ToString();
        AverageViewers = stats.AverageViewers.ToString("0.0");
        Followers = stats.FollowersGained.ToString();
        ChatAlerts = stats.ChatMessages + " / " + stats.AlertsPlayed;
    }

    private Task AddViewerSampleAsync()
    {
        if (!int.TryParse(ViewerSample.Trim(), out int viewers) ||
            AddViewerSampleRequestedAsync is null)
        {
            return Task.CompletedTask;
        }

        return AddViewerSampleRequestedAsync(viewers);
    }
}
