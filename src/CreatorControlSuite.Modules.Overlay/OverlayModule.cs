using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayModule(IOverlayDataService service) : IConnectableModule
{
    private bool _initialized;

    public string Id => "overlay";
    public string DisplayName => "Overlay";

    public IOverlayDataService Service { get; } = service;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Service.InitializeAsync(cancellationToken);
        _initialized = true;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return InitializeAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _initialized = false;
        return Task.CompletedTask;
    }

    public async Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        string path = await Service.GetDataFilePathAsync(cancellationToken);

        return new ModuleStatus(
            Id,
            DisplayName,
            _initialized
                ? ModuleHealth.Connected
                : ModuleHealth.Ready,
            File.Exists(path)
                ? path
                : "Overlay-Datendatei wird vorbereitet",
            DateTimeOffset.Now);
    }
}
