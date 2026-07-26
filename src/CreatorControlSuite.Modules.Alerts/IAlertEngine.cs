using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Modules.Alerts;

public interface IAlertEngine : IAsyncDisposable
{
    event EventHandler<AlertPlaybackState>? StateChanged;

    AlertPlaybackState State { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task EnqueueAsync(
        AlertRequest request,
        CancellationToken cancellationToken = default);

    Task ClearQueueAsync(
        CancellationToken cancellationToken = default);

    Task StopCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<AlertPreview> BuildPreviewAsync(
        string type,
        string user,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default);
}
