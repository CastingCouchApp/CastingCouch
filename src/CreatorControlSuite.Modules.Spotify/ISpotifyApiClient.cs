using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public interface ISpotifyApiClient
{
    void Configure(string accessToken);

    Task<string> GetCurrentUserDisplayNameAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotifyDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<SpotifyPlaybackState> GetPlaybackStateAsync(
        CancellationToken cancellationToken = default);

    Task<SpotifyQueue> GetQueueAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotifyRecentlyPlayedItem>> GetRecentlyPlayedAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);


    Task<IReadOnlyList<SpotifyTrack>> GetSavedTracksAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<bool> IsTrackSavedAsync(
        string trackId,
        CancellationToken cancellationToken = default);

    Task SaveTrackAsync(
        string trackId,
        CancellationToken cancellationToken = default);

    Task RemoveSavedTrackAsync(
        string trackId,
        CancellationToken cancellationToken = default);

    Task AddToQueueAsync(
        string trackUri,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpotifyTrack>> GetPlaylistTracksAsync(
        string playlistId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task TransferPlaybackAsync(
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default);

    Task StartPlaybackAsync(
        string? deviceId,
        string? contextUri,
        string? offsetTrackUri = null,
        CancellationToken cancellationToken = default);

    Task PlayTrackAsync(
        string trackUri,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task PausePlaybackAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SetVolumeAsync(
        int volumePercent,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SetShuffleAsync(
        bool enabled,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SetRepeatAsync(
        string repeatMode,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SeekPlaybackAsync(
        int positionMs,
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SkipNextAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);

    Task SkipPreviousAsync(
        string? deviceId,
        CancellationToken cancellationToken = default);
}
