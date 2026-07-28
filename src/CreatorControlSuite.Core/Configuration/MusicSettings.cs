namespace CreatorControlSuite.Core.Configuration;

public sealed class SpotifySettings
{
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "http://127.0.0.1:43821/callback/";
    public bool AutoConnect { get; set; } = true;
    public bool ConnectOnPrepare { get; set; } = true;
    public string ExecutablePath { get; set; } = "";
    public string PreferredDeviceId { get; set; } = "";
    public bool AutoTransferToPreferredDevice { get; set; } = true;
    public bool UseActiveDeviceWhenPreferredUnavailable { get; set; } = true;
    public bool SmartAutomationEnabled { get; set; } = true;
    public bool HealthMonitorEnabled { get; set; } = true;
    public bool AutoRecoverPlayback { get; set; } = true;
    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public List<SpotifyAutomationRuleSettings> AutomationRules { get; set; } = [];
    public string StartPlaylistUri { get; set; } = "";
    public List<string> FavoritePlaylistUris { get; set; } = [];
    public List<string> RecentPlaylistUris { get; set; } = [];
    public bool ShuffleSelectedPlaylist { get; set; }
    public int StartVolumePercent { get; set; } = 100;
    public bool FadeInEnabled { get; set; } = true;
    public int FadeInSeconds { get; set; } = 3;
    public bool FadeOutEnabled { get; set; } = true;
    public int FadeOutSeconds { get; set; } = 3;
    // Spotify-Inhalte werden im Overlay immer vollständig angezeigt.
    // Die alten Eigenschaften bleiben zur Abwärtskompatibilität erhalten.
    public bool OverlayShowTitle { get; set; } = true;
    public bool OverlayShowArtist { get; set; } = true;
    public bool OverlayShowAlbumCover { get; set; } = true;
    public bool OverlayShowProgress { get; set; } = true;
    public bool OverlayHideWhenPaused { get; set; } = false;
    public bool OverlayHideWhenMuted { get; set; } = true;
    public bool OverlayMuteDetectionSpotifyVolume { get; set; } = true;
    public bool OverlayMuteDetectionObsSource { get; set; } = true;
    public string OverlayObsAudioSource { get; set; } = "Spotify";
    public int OverlayShowAfterTrackChangeSeconds { get; set; } = 0;
    public bool PauseAfterFadeOut { get; set; } = true;
    public bool MuteOnLiveTransition { get; set; }
    public bool SetVolumeOnLiveTransition { get; set; } = true;
    public int LiveVolumePercent { get; set; } = 75;
    public bool MuteDuringAlerts { get; set; } = true;
    public int AlertMuteVolumePercent { get; set; } = 75;
    public string AlertDuckingMode { get; set; } = "Duck";
    public int AlertFadeOutMilliseconds { get; set; } = 500;
    public int AlertFadeInMilliseconds { get; set; } = 500;
    public int FadeTargetVolumePercent { get; set; } = 35;
    public bool OverlayEnabled { get; set; } = true;
    public string OverlayObsScene { get; set; } = "";
    public string OverlayObsSource { get; set; } = "ccs_spotify";
    public string[] Scopes { get; set; } =
    [
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-modify-playback-state",
        "user-read-recently-played",
        "playlist-read-private",
        "playlist-read-collaborative",
        "user-library-read",
        "user-library-modify"
    ];
}

public sealed class MusicPlayerSettings
{
    /// <summary>Aktiver Music-Provider: spotify | ytmusic. Immer nur einer aktiv.</summary>
    public string ProviderId { get; set; } = "spotify";
}

public sealed class YouTubeMusicSettings
{
    public int BridgePort { get; set; } = 43831;
    public bool AutoConnect { get; set; } = true;
    public bool ConnectOnPrepare { get; set; } = true;
    /// <summary>Sekunden ohne State vom Bookmarklet, bevor die Verbindung als inaktiv gilt.</summary>
    public int StateTimeoutSeconds { get; set; } = 12;
}

public sealed class SpotifyAutomationRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neue Spotify-Regel";
    public bool Enabled { get; set; } = true;
    public string TriggerType { get; set; } = "ObsSceneChanged";
    public string TriggerValue { get; set; } = "";
    public string ActionType { get; set; } = "Resume";
    public string PlaylistUri { get; set; } = "";
    public bool Shuffle { get; set; } = true;
    public int VolumePercent { get; set; } = 75;
    public int DelaySeconds { get; set; }
}

