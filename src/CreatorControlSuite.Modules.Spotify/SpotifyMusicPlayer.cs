using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyMusicPlayer(SpotifyModule spotifyModule) : IMusicPlayer
{
    private readonly SpotifyModule _spotifyModule = spotifyModule;

    public string Id => MusicProviderIds.Spotify;
    public string DisplayName => "Spotify";
    public bool SupportsSeek => true;
    public bool SupportsVolume => true;

    public Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        SpotifySnapshot snapshot = _spotifyModule.GetSnapshot();
        return Task.FromResult(Map(snapshot));
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.ResumeAsync(cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.PauseAsync(cancellationToken);

    public async Task PlayPauseAsync(CancellationToken cancellationToken = default)
        => await _spotifyModule.PlayPauseAsync(cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.NextAsync(cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.PreviousAsync(cancellationToken);

    public Task SeekAsync(int positionMs, CancellationToken cancellationToken = default)
        => _spotifyModule.SeekAsync(positionMs, cancellationToken);

    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
        => _spotifyModule.SetVolumeAsync(volumePercent, cancellationToken);

    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _spotifyModule.DisconnectAsync(cancellationToken);

    private static NowPlayingSnapshot Map(SpotifySnapshot snapshot)
    {
        SpotifyTrack? track = snapshot.Playback.Track;
        bool connected = snapshot.Authenticated;
        string status = !connected
            ? "Nicht verbunden"
            : track is null
                ? "Verbunden · Kein Titel"
                : snapshot.Playback.IsPlaying
                    ? "Spielt"
                    : "Pause";

        return new NowPlayingSnapshot(
            MusicProviderIds.Spotify,
            Connected: connected,
            IsPlaying: snapshot.Playback.IsPlaying,
            Title: track?.Name ?? "",
            Artist: track?.Artist ?? "",
            Album: track?.Album ?? "",
            CoverUrl: track?.AlbumImageUrl ?? "",
            ProgressMs: Math.Max(0, snapshot.Playback.ProgressMs),
            DurationMs: Math.Max(0, track?.DurationMs ?? 0),
            VolumePercent: snapshot.Playback.Device?.VolumePercent,
            StatusText: status);
    }
}
