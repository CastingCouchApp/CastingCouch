using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed partial class SpotifyApiClient
{
    private static SpotifyDevice ToDevice(DeviceResponse device) =>
        new(
            device.Id ?? "",
            device.Name,
            device.Type,
            device.IsActive,
            device.IsPrivateSession,
            device.IsRestricted,
            device.VolumePercent ?? 0,
            device.SupportsVolume);

    private static SpotifyTrack ToTrack(TrackResponse track) =>
        new(
            track.Id,
            track.Uri,
            track.Name,
            string.Join(
                ", ",
                track.Artists.Select(artist => artist.Name)),
            track.Album?.Name ?? "",
            track.Album?.Images.FirstOrDefault()?.Url ?? "",
            track.DurationMs);

    private static string ToTrackUri(string trackIdOrUri)
    {
        if (string.IsNullOrWhiteSpace(trackIdOrUri))
        {
            throw new ArgumentException(
                "Spotify-Titel-ID fehlt.",
                nameof(trackIdOrUri));
        }

        string value = trackIdOrUri.Trim();
        return value.StartsWith(
            "spotify:track:",
            StringComparison.OrdinalIgnoreCase)
            ? value
            : "spotify:track:" + value;
    }
}
