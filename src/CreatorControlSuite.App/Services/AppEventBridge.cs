using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.App.Services;

/// <summary>
/// Bridges domain service events onto the application <see cref="IEventBus"/>.
/// Started once after host construction (App startup or MainWindow ctor).
/// </summary>
public sealed class AppEventBridge(
    IEventBus eventBus,
    IStreamWorkflowService workflow,
    IMusicPlayerRouter musicRouter) : IDisposable
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly IStreamWorkflowService _workflow = workflow;
    private readonly IMusicPlayerRouter _musicRouter = musicRouter;
    private bool _started;
    private bool _disposed;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _workflow.StateChanged += OnWorkflowStateChanged;
        _musicRouter.SnapshotChanged += OnMusicSnapshotChanged;
        _musicRouter.ActiveProviderChanged += OnActiveProviderChanged;
        _started = true;
    }

    private void OnWorkflowStateChanged(object? sender, WorkflowState state)
    {
        _eventBus.Publish(new WorkflowPhaseChanged(
            state.Phase,
            state.Detail,
            DateTimeOffset.Now));
    }

    private void OnMusicSnapshotChanged(object? sender, EventArgs e)
    {
        // Lightweight signal; full snapshot is published by MusicPlayerUiPresenter.GetStateAsync.
        _eventBus.Publish(new ModuleConnectionChanged(
            "music",
            Connected: true,
            Detail: "snapshot-changed:" + _musicRouter.ActiveProviderId,
            DateTimeOffset.Now));
    }

    private void OnActiveProviderChanged(object? sender, EventArgs e)
    {
        _eventBus.Publish(new ModuleConnectionChanged(
            "music-provider",
            Connected: true,
            Detail: _musicRouter.ActiveProviderId,
            DateTimeOffset.Now));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_started)
        {
            _workflow.StateChanged -= OnWorkflowStateChanged;
            _musicRouter.SnapshotChanged -= OnMusicSnapshotChanged;
            _musicRouter.ActiveProviderChanged -= OnActiveProviderChanged;
        }

        _disposed = true;
    }
}
