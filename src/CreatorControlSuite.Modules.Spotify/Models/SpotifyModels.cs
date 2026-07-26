namespace CreatorControlSuite.Modules.Spotify.Models;

public sealed record SpotifyTokenSet(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string TokenType,
    IReadOnlyList<string> Scopes,
    DateTimeOffset ObtainedAt)
{
    public DateTimeOffset ExpiresAt =>
        ObtainedAt.AddSeconds(Math.Max(0, ExpiresInSeconds - 60));
}

public sealed record SpotifyDevice(
    string Id,
    string Name,
    string Type,
    bool IsActive,
    bool IsPrivateSession,
    bool IsRestricted,
    int VolumePercent,
    bool SupportsVolume);

public sealed record SpotifyTrack(
    string Id,
    string Uri,
    string Name,
    string Artist,
    string Album,
    string AlbumImageUrl,
    int DurationMs);

public sealed record SpotifyPlaybackState(
    bool HasPlayback,
    bool IsPlaying,
    bool ShuffleEnabled,
    string RepeatMode,
    int ProgressMs,
    SpotifyDevice? Device,
    SpotifyTrack? Track,
    string ContextUri);

public sealed record SpotifyQueue(
    SpotifyTrack? CurrentlyPlaying,
    IReadOnlyList<SpotifyTrack> Upcoming);

public sealed record SpotifyRecentlyPlayedItem(
    SpotifyTrack Track,
    DateTimeOffset PlayedAt);

public sealed record SpotifyPlaylist(
    string Id,
    string Uri,
    string Name,
    string OwnerName,
    string ImageUrl,
    int TrackCount);

public sealed record SpotifySnapshot(
    bool Authenticated,
    string UserDisplayName,
    IReadOnlyList<SpotifyDevice> Devices,
    SpotifyPlaybackState Playback,
    IReadOnlyList<SpotifyPlaylist> Playlists,
    SpotifyQueue Queue,
    IReadOnlyList<SpotifyRecentlyPlayedItem> RecentlyPlayed,
    IReadOnlyList<SpotifyTrack> SavedTracks);
