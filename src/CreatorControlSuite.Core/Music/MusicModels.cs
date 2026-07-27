namespace CreatorControlSuite.Core.Music;

public static class MusicProviderIds
{
    public const string Spotify = "spotify";
    public const string YouTubeMusic = "ytmusic";

    public static string Normalize(string? providerId)
    {
        if (string.Equals(providerId, YouTubeMusic, StringComparison.OrdinalIgnoreCase))
        {
            return YouTubeMusic;
        }

        return Spotify;
    }

    public static string DisplayName(string? providerId) =>
        Normalize(providerId) switch
        {
            YouTubeMusic => "YouTube Music",
            _ => "Spotify"
        };
}

public sealed record NowPlayingSnapshot(
    string ProviderId,
    bool Connected,
    bool IsPlaying,
    string Title,
    string Artist,
    string Album,
    string CoverUrl,
    int ProgressMs,
    int DurationMs,
    int? VolumePercent,
    string StatusText)
{
    public static NowPlayingSnapshot Empty(string providerId) =>
        new(
            MusicProviderIds.Normalize(providerId),
            Connected: false,
            IsPlaying: false,
            Title: "",
            Artist: "",
            Album: "",
            CoverUrl: "",
            ProgressMs: 0,
            DurationMs: 0,
            VolumePercent: null,
            StatusText: "Nicht verbunden");
}
