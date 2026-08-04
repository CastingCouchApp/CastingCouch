using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Services;

public static class SpotifyPlaybackLevelResolver
{
    public static int Resolve(
        SpotifySnapshot snapshot,
        int fallbackLevel,
        int? requestedLevel = null,
        DateTimeOffset? requestedAt = null,
        DateTimeOffset? now = null)
    {
        if (requestedLevel.HasValue &&
            requestedAt.HasValue &&
            (now ?? DateTimeOffset.UtcNow) - requestedAt.Value < TimeSpan.FromSeconds(4))
        {
            return Math.Clamp(requestedLevel.Value, 0, 100);
        }

        SpotifyDevice? playbackDevice = snapshot.Playback.Device;
        SpotifyDevice? activeDevice = snapshot.Devices.FirstOrDefault(device =>
            device.IsActive &&
            (playbackDevice is null ||
             string.Equals(device.Id, playbackDevice.Id, StringComparison.Ordinal)));

        return Math.Clamp(
            playbackDevice?.VolumePercent ??
            activeDevice?.VolumePercent ??
            fallbackLevel,
            0,
            100);
    }
}
