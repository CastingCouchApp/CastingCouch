using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Modules.YouTubeMusic;

public sealed class YouTubeMusicModule : IConnectableModule, IMusicPlayer
{
    private readonly ISettingsStore _settingsStore;
    private readonly YouTubeMusicBridge _bridge;
    private bool _connected;

    public YouTubeMusicModule(
        ISettingsStore settingsStore,
        YouTubeMusicBridge bridge)
    {
        _settingsStore = settingsStore;
        _bridge = bridge;
        _bridge.StateChanged += (_, _) => SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Id => MusicProviderIds.YouTubeMusic;
    public string DisplayName => "YouTube Music";
    public bool SupportsSeek => false;
    public bool SupportsVolume => false;

    public event EventHandler? SnapshotChanged;

    public Task InitializeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<ModuleStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        NowPlayingSnapshot snapshot = await GetSnapshotAsync(cancellationToken);
        ModuleHealth health = !_connected
            ? ModuleHealth.Ready
            : snapshot.Connected && !string.Equals(snapshot.StatusText, "Bookmarklet inaktiv", StringComparison.Ordinal)
                ? ModuleHealth.Connected
                : ModuleHealth.Degraded;

        return new ModuleStatus(
            Id,
            DisplayName,
            health,
            snapshot.StatusText,
            DateTimeOffset.UtcNow);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _bridge.StartAsync(cancellationToken);
        _connected = true;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _bridge.StopAsync();
        _connected = false;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        if (!_connected)
        {
            return NowPlayingSnapshot.Empty(MusicProviderIds.YouTubeMusic);
        }

        return _bridge.GetSnapshot(settings.YouTubeMusic.StateTimeoutSeconds);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _bridge.EnqueueCommand("play");
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _bridge.EnqueueCommand("pause");
        return Task.CompletedTask;
    }

    public Task PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _bridge.EnqueueCommand("playpause");
        return Task.CompletedTask;
    }

    public Task NextAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _bridge.EnqueueCommand("next");
        return Task.CompletedTask;
    }

    public Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _bridge.EnqueueCommand("previous");
        return Task.CompletedTask;
    }

    public Task SeekAsync(int positionMs, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("Seek wird von YouTube Music nicht unterstützt."));

    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("Lautstärke wird von YouTube Music nicht unterstützt."));

    public async Task<string> GetBookmarkletAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        return _bridge.GetBookmarklet(settings.YouTubeMusic.BridgePort);
    }

    public async Task<string> GetBookmarkletInstallPageUrlAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        return _bridge.GetBookmarkletInstallPageUrl(settings.YouTubeMusic.BridgePort);
    }

    public string GetBookmarkletDisplayName()
        => _bridge.GetBookmarkletDisplayName();

    public bool IsBridgeRunning => _bridge.IsRunning;

    private void EnsureConnected()
    {
        if (!_connected || !_bridge.IsRunning)
        {
            throw new InvalidOperationException("YouTube Music Bridge ist nicht gestartet.");
        }
    }
}
