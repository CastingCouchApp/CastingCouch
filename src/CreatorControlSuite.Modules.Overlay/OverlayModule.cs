using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayModule : IConnectableModule
{
    private readonly IOverlayDataService _service;
    private bool _initialized;

    public OverlayModule(IOverlayDataService service)
    {
        _service = service;
    }

    public string Id => "overlay";
    public string DisplayName => "Overlay";

    public IOverlayDataService Service => _service;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _service.InitializeAsync(cancellationToken);
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
        var path = await _service.GetDataFilePathAsync(cancellationToken);

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
