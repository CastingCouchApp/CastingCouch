namespace CreatorControlSuite.Core.Ipc;

public sealed record IpcCommand(
    string Id,
    string Command,
    IReadOnlyDictionary<string,string> Arguments,
    DateTimeOffset SentAt);

public sealed record IpcResponse(
    string Id,
    bool Success,
    string Message,
    IReadOnlyDictionary<string,string> Data);

public static class IpcCommandNames
{
    public const string Ping = "system.ping";
    public const string Status = "system.status";
    public const string Prepare = "workflow.prepare";
    public const string Countdown = "workflow.countdown";
    public const string Live = "workflow.live";
    public const string Pause = "workflow.pause";
    public const string Resume = "workflow.resume";
    public const string End = "workflow.end";
    public const string AlertTest = "alert.test";
    public const string ExternalAlertStart = "alert.external.start";
    public const string ExternalAlertEnd = "alert.external.end";
    public const string ExternalAlertClear = "alert.external.clear";
    public const string ObsScene = "obs.scene";
    public const string ObsMute = "obs.mute";
    public const string SpotifyPlay = "spotify.play";
    public const string SpotifyPause = "spotify.pause";
    public const string SpotifyToggle = "spotify.toggle";
    public const string SpotifyNext = "spotify.next";
    public const string SpotifyPrevious = "spotify.previous";
    public const string SpotifyVolume = "spotify.volume";
    public const string SpotifyVolumeUp = "spotify.volumeup";
    public const string SpotifyVolumeDown = "spotify.volumedown";
    public const string SpotifyVolume25 = "spotify.volume25";
    public const string SpotifyVolume50 = "spotify.volume50";
    public const string SpotifyPlaylist = "spotify.playlist";
    public const string StreamStart = "stream.start";
    public const string StreamStop = "stream.stop";
}
