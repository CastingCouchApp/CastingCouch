using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Modules.Overlay.Assets;
using CreatorControlSuite.Modules.Overlay.Extensions;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayModule(
    IOverlayDataService service,
    IOverlayWebServer webServer,
    IOverlayLayoutStore layoutStore,
    IOverlayExtensionStore extensionStore,
    IOverlayAssetStore assetStore,
    IChatHistoryService chatHistory) : IConnectableModule
{
    private bool _initialized;

    public string Id => "overlay";
    public string DisplayName => "Overlay";

    public IOverlayDataService Service { get; } = service;
    public IOverlayWebServer WebServer { get; } = webServer;
    public IOverlayLayoutStore LayoutStore { get; } = layoutStore;
    public IOverlayExtensionStore ExtensionStore { get; } = extensionStore;
    public IOverlayAssetStore AssetStore { get; } = assetStore;
    public IChatHistoryService ChatHistory { get; } = chatHistory;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Service.InitializeAsync(cancellationToken);
        try
        {
            await WebServer.StartAsync(cancellationToken);
        }
        catch
        {
            // Overlay-Daten sind auch ohne Webserver nutzbar; Status meldet den Fehler.
        }

        try
        {
            await ChatHistory.InitializeAsync(cancellationToken);
        }
        catch
        {
            // Chat-History ist best-effort.
        }

        _initialized = true;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return InitializeAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ChatHistory.FlushAsync(cancellationToken);
        }
        catch
        {
            // best-effort
        }

        await WebServer.StopAsync(cancellationToken);
        _initialized = false;
    }

    public async Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        string path = await Service.GetDataFilePathAsync(cancellationToken);
        string detail;
        if (WebServer.IsRunning && !string.IsNullOrWhiteSpace(WebServer.BaseUrl))
        {
            detail = $"Webserver {WebServer.BaseUrl} · {path}";
        }
        else if (File.Exists(path))
        {
            detail = path;
        }
        else
        {
            detail = "Overlay-Datendatei wird vorbereitet";
        }

        return new ModuleStatus(
            Id,
            DisplayName,
            _initialized
                ? ModuleHealth.Connected
                : ModuleHealth.Ready,
            detail,
            DateTimeOffset.Now);
    }
}
