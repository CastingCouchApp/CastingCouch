using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Modules.Workflow;

public interface IStreamWorkflowService
{
    WorkflowState State { get; }
    StreamSessionStats SessionStats { get; }

    event EventHandler<WorkflowState>? StateChanged;

    Task PrepareAsync(CancellationToken cancellationToken = default);
    Task StartCountdownAsync(CancellationToken cancellationToken = default);
    Task StartCountdownAsync(int durationSeconds, CancellationToken cancellationToken = default);
    Task StopCountdownAsync(CancellationToken cancellationToken = default);
    Task GoLiveAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task EndAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
    Task ResetSessionStatsAsync(
        DateTimeOffset? startedAt = null,
        CancellationToken cancellationToken = default);
    Task FinalizeSessionStatsAsync(
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default);

    Task AddViewerSampleAsync(
        int viewers,
        CancellationToken cancellationToken = default);

    Task SetFollowerCountsAsync(
        int start,
        int current,
        CancellationToken cancellationToken = default);

    Task RegisterChatMessageAsync(
        CancellationToken cancellationToken = default);

    Task RegisterAlertPlayedAsync(
        CancellationToken cancellationToken = default);

    Task RegisterTwitchEventAsync(
        string eventType,
        int count = 1,
        CancellationToken cancellationToken = default);
}
