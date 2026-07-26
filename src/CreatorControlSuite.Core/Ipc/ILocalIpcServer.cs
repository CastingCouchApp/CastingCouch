namespace CreatorControlSuite.Core.Ipc;

public interface ILocalIpcServer : IAsyncDisposable
{
    bool IsRunning { get; }
    event EventHandler<bool>? StateChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
