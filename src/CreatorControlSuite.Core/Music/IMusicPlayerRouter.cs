namespace CreatorControlSuite.Core.Music;

public interface IMusicPlayerRouter
{
    string ActiveProviderId { get; }
    string ActiveDisplayName { get; }
    IMusicPlayer ActivePlayer { get; }
    IReadOnlyList<IMusicPlayer> Players { get; }

    event EventHandler? ActiveProviderChanged;
    event EventHandler? SnapshotChanged;

    Task ApplyProviderAsync(string providerId, CancellationToken cancellationToken = default);
    Task RefreshFromSettingsAsync(CancellationToken cancellationToken = default);
    Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task PlayPauseAsync(CancellationToken cancellationToken = default);
    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(int positionMs, CancellationToken cancellationToken = default);
    Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
    Task ConnectActiveAsync(CancellationToken cancellationToken = default);
    Task DisconnectActiveAsync(CancellationToken cancellationToken = default);
    void NotifySnapshotChanged();
}
