namespace CreatorControlSuite.Core.Music;

public interface IMusicPlayer
{
    string Id { get; }
    string DisplayName { get; }
    bool SupportsSeek { get; }
    bool SupportsVolume { get; }

    Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task PlayPauseAsync(CancellationToken cancellationToken = default);
    Task NextAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(int positionMs, CancellationToken cancellationToken = default);
    Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
