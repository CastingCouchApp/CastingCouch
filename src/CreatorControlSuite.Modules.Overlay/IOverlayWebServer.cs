namespace CreatorControlSuite.Modules.Overlay;

public interface IOverlayWebServer
{
    bool IsRunning { get; }
    int Port { get; }
    string? BaseUrl { get; }
    string? RootPath { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
}
