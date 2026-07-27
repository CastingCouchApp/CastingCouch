namespace CreatorControlSuite.Core.Configuration;

public sealed class AppSettings
{
    public List<string> AdditionalScenes { get; set; } = [];

    public const string SectionName = "CreatorControlSuite";

    public ProductSettings Product { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public BrandingSettings Branding { get; set; } = new();
    public ObsSettings Obs { get; set; } = new();
    public TwitchSettings Twitch { get; set; } = new();
    public SpotifySettings Spotify { get; set; } = new();
    public MusicPlayerSettings MusicPlayer { get; set; } = new();
    public YouTubeMusicSettings YouTubeMusic { get; set; } = new();
    public StreamerBotSettings StreamerBot { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public StreamerHudSettings StreamerHud { get; set; } = new();
    public WorkflowSettings Workflow { get; set; } = new();
    public StreamDeckSettings StreamDeck { get; set; } = new();
    public DashboardSettings Dashboard { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
}

public sealed class ProductSettings
{
    public string ProductName { get; set; } = "Creator Control Suite";
    public string Version { get; set; } = "0.0.0";
    public string UpdateChannel { get; set; } = "Alpha";
}

public sealed class GeneralSettings
{
    public string Language { get; set; } = "de-DE";
    public string ThemeId { get; set; } = "classic";
    public string DataRoot { get; set; } = "";
    public string BackupRoot { get; set; } = "";
    public string OverlayManifestPath { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ConnectionWatchdogEnabled { get; set; } = true;
    public int ConnectionWatchdogSeconds { get; set; } = 15;
    public bool ReconnectObs { get; set; } = true;
    public bool ReconnectTwitch { get; set; } = true;
    public bool ReconnectSpotify { get; set; } = true;
    public bool ReconnectYouTubeMusic { get; set; } = true;
    public bool ReconnectStreamerBot { get; set; } = true;
}

public sealed class BrandingSettings
{
    public string DisplayName { get; set; } = "Mein Stream";
    public string ChannelName { get; set; } = "";
    public string AccentColor { get; set; } = "#FF8C00";
    public string LogoPath { get; set; } = "";
}

public sealed class ObsSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4455;
    public bool AutoConnect { get; set; } = true;
    public bool ConnectOnPrepare { get; set; } = true;
    public string ExecutablePath { get; set; } = "";
    public string StartScene { get; set; } = "Start";
    public string LiveScene { get; set; } = "Game";
    public string PauseScene { get; set; } = "Pause";
    public string EndScene { get; set; } = "Ende";
    public string GoalOverlayScene { get; set; } = "CCS Ziele & Overlay-Daten";
    public string MicrophoneSource { get; set; } = "";
    public string DesktopAudioSource { get; set; } = "";
    public string MusicSource { get; set; } = "";
    public string CameraSource { get; set; } = "";
    public List<ObsAudioProfileSettings> AudioProfiles { get; set; } = [];
}

public sealed class ObsAudioProfileSettings
{
    public string Name { get; set; } = "Neues Profil";
    public List<ObsAudioProfileEntrySettings> Inputs { get; set; } = [];
}

public sealed class ObsAudioProfileEntrySettings
{
    public string InputName { get; set; } = "";
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public string MonitorType { get; set; } = "OBS_MONITORING_TYPE_NONE";
    public int SyncOffsetMilliseconds { get; set; }
}

public sealed class TwitchGoalSettings
{
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "";
    public double Current { get; set; }
    public double Target { get; set; } = 100;
    public string FontFace { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 36;
    public string Currency { get; set; } = "EUR";
}

public sealed class TwitchSettings
{
    public string ClientId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public bool ConnectOnPrepare { get; set; } = true;
    public string CreatorDashboardUrl { get; set; } = "";
    public bool EnableChat { get; set; } = true;
    public bool EnableEventSub { get; set; } = true;
    public bool UseDeviceCodeFlow { get; set; } = true;
    public TwitchGoalSettings FollowerGoal { get; set; } = new() { Title = "Follower-Ziel", Target = 200 };
    public TwitchGoalSettings SubGoal { get; set; } = new() { Title = "Sub-Ziel", Target = 25 };
    public TwitchGoalSettings DonationGoal { get; set; } = new() { Title = "Donation-Ziel", Target = 100, Currency = "EUR" };
    public int EndSceneDurationSeconds { get; set; } = 60;
    public bool RaidOnStreamEnd { get; set; }

    /// <summary>Zuletzt gewählter Ablauf im Streamende-Dialog.</summary>
    public StreamEndMode StreamEndMode { get; set; } = StreamEndMode.EndSceneThenStop;

    public int RaidCountdownSeconds { get; set; } = 90;

    /// <summary>
    /// How long after the end scene the app keeps polling/retrying Start Raid
    /// before finishing the stream without a raid.
    /// </summary>
    public int RaidStartTimeoutSeconds { get; set; } = 120;

    public bool StopStreamAfterRaid { get; set; } = true;
    public bool StopSpotifyAfterRaid { get; set; } = true;
    public int PlannedStreamEndSeconds { get; set; }
    public int PlannedStreamEndMinutes { get; set; } = 30;
    public string LiveNotificationText { get; set; } = "";
    public string SelectedRaidChannel { get; set; } = "";
    public List<string> RaidChannels { get; set; } = [];

    public string[] Scopes { get; set; } =
    [
        "user:read:chat",
        "user:write:chat",
        "user:bot",
        "channel:bot",
        "channel:manage:broadcast",
        "channel:manage:raids",
        "moderator:read:followers",
        "user:read:follows",
        "moderator:read:chatters",
        "moderator:manage:banned_users",
        "channel:read:subscriptions",
        "bits:read",
        "channel:read:redemptions",
        "channel:manage:redemptions",
        "channel:read:guest_star",
        "channel:manage:polls",
        "channel:manage:predictions"
    ];
}

public sealed class StreamerBotSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public string Endpoint { get; set; } = "/";
    public string Password { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public bool ConnectOnPrepare { get; set; } = true;
    public string ExecutablePath { get; set; } = "";
    public bool SuppressAlertActionsWhenSuiteAlertsEnabled { get; set; } = false;
    public string DisableAlertsActionName { get; set; } = "CCS Alerts deaktivieren";
    public string DisableAlertsActionId { get; set; } = "";
    public string EnableAlertsActionName { get; set; } = "CCS Alerts aktivieren";
    public string EnableAlertsActionId { get; set; } = "";
}

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
    public string OverlayProjectId { get; set; } = "";
    public string OverlayItemId { get; set; } = "";
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

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public string ObsSceneName { get; set; } = "_alerts";
    public string ObsMediaSourceName { get; set; } = "ccs_alert_media";
    public string ObsTextSourceName { get; set; } = "ccs_alert_text";
    public string AudioOutputDeviceId { get; set; } = "";
    public int QueueCapacity { get; set; } = 250;
    public int InterAlertDelayMilliseconds { get; set; } = 350;
    public bool StopPreviousMediaBeforeNext { get; set; } = true;
    public bool AutoCreateObsSources { get; set; } = false;
    public Dictionary<string, AlertDefinitionSettings> Definitions { get; set; } =
        AlertDefinitionSettings.CreateDefaults();
}

public sealed class AlertDefinitionSettings
{
    public string Type { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string TextTemplate { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string SoundPath { get; set; } = "";
    public int DurationSeconds { get; set; } = 8;
    public int Priority { get; set; } = 100;
    public string FontFace { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 44;
    public string FontColor { get; set; } = "#FFFFFF";
    public string Animation { get; set; } = "Fade";
    public int X { get; set; } = 510;
    public int Y { get; set; } = 690;
    public int Width { get; set; } = 900;
    public int Height { get; set; } = 260;
    public int VolumePercent { get; set; } = 100;
    public double SoundStartSeconds { get; set; } = 0;
    public double SoundEndSeconds { get; set; } = 0;
    public string AudioOutputDeviceId { get; set; } = "";

    public static Dictionary<string, AlertDefinitionSettings> CreateDefaults()
    {
        return new Dictionary<string, AlertDefinitionSettings>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Follow"] = new()
            {
                Type = "Follow",
                TextTemplate = "{user} folgt jetzt!",
                DurationSeconds = 8,
                Priority = 100
            },
            ["Sub"] = new()
            {
                Type = "Sub",
                TextTemplate = "{user} hat abonniert!",
                DurationSeconds = 9,
                Priority = 80
            },
            ["ReSub"] = new()
            {
                Type = "ReSub",
                TextTemplate = "{user} ist seit {months} Monaten dabei!",
                DurationSeconds = 9,
                Priority = 75
            },
            ["GiftSub"] = new()
            {
                Type = "GiftSub",
                TextTemplate = "{user} verschenkt {count} Subs!",
                DurationSeconds = 10,
                Priority = 70
            },
            ["Cheer"] = new()
            {
                Type = "Cheer",
                TextTemplate = "{user} cheeret {bits} Bits!",
                DurationSeconds = 9,
                Priority = 85
            },
            ["Raid"] = new()
            {
                Type = "Raid",
                TextTemplate = "Raid von {user} mit {viewers} Zuschauern!",
                DurationSeconds = 12,
                Priority = 10,
                Animation = "Slide"
            }
        };
    }
}

public sealed class OverlaySettings
{
    public string RootPath { get; set; } = "";
    public bool UseBundledOverlay { get; set; } = true;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int RefreshMilliseconds { get; set; } = 500;
    public string DataFileName { get; set; } = "overlay-data.json";
    // Optionaler vollständiger Pfad zu der JSON-Datei, die vorhandene Overlays bereits lesen.
    // Leer bedeutet: automatische Standarddatei unter %LocalAppData%\CreatorControlSuite\Overlay\data.
    public string DataFilePath { get; set; } = "";
    // Nur noch für die rückwärtskompatible Deserialisierung älterer
    // Einstellungen vorhanden. Neue Versionen spiegeln keine Datendateien
    // mehr, sondern verknüpfen Overlay-Projekte mit der zentralen Datei.
    public List<string> AdditionalDataRoots { get; set; } = [];
    public bool AutoInstallBrowserSources { get; set; } = true;
    public bool EnableFollowerGoal { get; set; } = true;
    public bool EnableSpotifyWidget { get; set; } = true;
    public bool EnableLiveStatusWidget { get; set; } = true;
    public bool EnableEndStatsWidget { get; set; } = true;
    public string StartText { get; set; } = "Der Stream startet gleich";
    public string PauseText { get; set; } = "Kurze Pause – gleich geht es weiter";
    public string EndText { get; set; } = "Danke fürs Zuschauen";
    public string SharedSceneText { get; set; } = "";
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 54;
    public string FontColor { get; set; } = "#FFFFFF";
    public int StartTimerSeconds { get; set; } = 600;
    public int TimerX { get; set; } = 760;
    public int TimerY { get; set; } = 700;
    public string FrameStyle { get; set; } = "Solid";
    public string FrameColor { get; set; } = "#FF6A00";
    public string FrameEffect { get; set; } = "Glow";
}

public sealed class StreamerHudSettings
{
    public bool Enabled { get; set; }
    public int MonitorIndex { get; set; }
    public double Opacity { get; set; } = 0.85;
    public bool ClickThrough { get; set; } = true;
    public bool ShowChat { get; set; } = true;
    public bool ShowEvents { get; set; } = true;
    public bool ShowLiveStatus { get; set; } = true;
    public string Anchor { get; set; } = "TopRight";
    public int Margin { get; set; } = 24;
    public int PanelWidth { get; set; } = 420;
}

public sealed class WorkflowSettings
{
    public List<TimedAutomationRuleSettings> TimedAutomations { get; set; } = [];
    public List<RunOfShowStepSettings> RunOfShowSteps { get; set; } = [];
    public List<RunOfShowPlanSettings> RunOfShowPlans { get; set; } = [];
    public string ActiveRunOfShowPlanId { get; set; } = "";
    public int StartCountdownSeconds { get; set; } = 600;
    public int EndSceneSeconds { get; set; } = 60;
    public bool ExportSessionReport { get; set; } = true;
    public bool AutoPrepareNextStream { get; set; } = true;
    public bool AutoStartSpotifyPlaylist { get; set; } = true;
    public bool AutoFadeSpotifyOnLive { get; set; } = true;
    public bool AutoPlayEndMusic { get; set; } = false;
    public bool PauseSpotifyOnStreamEnd { get; set; } = true;
    public bool AutoSwitchScenes { get; set; } = false;
    public bool AutoStartObsStream { get; set; } = false;
    public bool AutoStopObsStream { get; set; } = true;
    public int ViewerSampleSeconds { get; set; } = 15;
}


public sealed class RunOfShowPlanSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Standard";
    public List<RunOfShowStepSettings> Steps { get; set; } = [];
}

public sealed class RunOfShowStepSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neuer Regieschritt";
    public bool Enabled { get; set; } = true;
    public string ObsScene { get; set; } = "";
    public string TransitionName { get; set; } = "";
    public int TransitionDurationMilliseconds { get; set; } = 1000;
    public string SpotifyAction { get; set; } = "None";
    public int SpotifyVolumePercent { get; set; } = 35;
    public string SpotifyPlaylistUri { get; set; } = "";
    public bool SpotifyPlaylistShuffle { get; set; } = true;
    public int SpotifyActionDelaySeconds { get; set; }
    public int SpotifyFadeSeconds { get; set; }
    public int SpotifyPriority { get; set; }
    public string StreamerBotActionId { get; set; } = "";
    public string StreamerBotActionName { get; set; } = "";
    public int ActionDelayMilliseconds { get; set; }
    public bool ContinueOnActionError { get; set; }
    public bool UpdateTwitchChannel { get; set; }
    public string TwitchTitle { get; set; } = "";
    public string TwitchCategoryId { get; set; } = "";
    public string TwitchCategoryName { get; set; } = "";
    public bool ContinueOnTwitchError { get; set; }
    public bool AutoAdvance { get; set; }
    public int AutoAdvanceDelaySeconds { get; set; } = 10;
}

public sealed class TimedAutomationRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neue Automatisierung";
    public bool Enabled { get; set; } = true;
    public string TriggerType { get; set; } = "SceneElapsed";
    public string TriggerScene { get; set; } = "";
    public int DelaySeconds { get; set; } = 10;
    public string ActionType { get; set; } = "SwitchScene";
    public string ObsScene { get; set; } = "";
    public string ObsSource { get; set; } = "";
    public bool SourceVisible { get; set; }
    public string TargetScene { get; set; } = "";
    public string TransitionName { get; set; } = "";
    public int TransitionDurationMilliseconds { get; set; } = 1000;
    public string SpotifyAction { get; set; } = "None";
    public int SpotifyVolumePercent { get; set; } = 35;
    public string SpotifyPlaylistUri { get; set; } = "";
    public bool SpotifyPlaylistShuffle { get; set; } = true;
    public int SpotifyActionDelaySeconds { get; set; }
    public int SpotifyFadeSeconds { get; set; }
    public int SpotifyPriority { get; set; }
    public string SpotifyAutomationGroup { get; set; } = "Standard";
    public bool SpotifyExclusiveGroup { get; set; } = true;
    public bool SpotifySavePreviousState { get; set; }
    public bool SpotifyAutoRestorePreviousState { get; set; }
    public int SpotifyAutoRestoreDelaySeconds { get; set; } = 30;
    public bool SpotifyAutoRestoreRequireSameScene { get; set; } = true;
    public bool SpotifyAutoRestoreRequireSameGroup { get; set; } = true;
    public bool SpotifyAutoRestoreRequireUnchangedPlayback { get; set; } = true;
    public bool ResetSourceAtStreamEnd { get; set; }
    public bool ResetSourceVisible { get; set; } = true;
    public bool OncePerStream { get; set; } = true;
    public string ObsInput { get; set; } = "";
    public bool InputMuted { get; set; } = true;
    public string StreamerBotActionId { get; set; } = "";
    public string StreamerBotActionName { get; set; } = "";
    public string ConditionType { get; set; } = "None";
    public string ConditionValue { get; set; } = "";
    public bool ConditionNegated { get; set; }
    public string NextRuleId { get; set; } = "";
    public int NextRuleDelaySeconds { get; set; }
    public bool ContinueChainOnError { get; set; }
    public int Priority { get; set; }
    public string ExecutionMode { get; set; } = "SkipIfRunning";
    public int TimeoutSeconds { get; set; } = 60;
    public string ScheduleTime { get; set; } = "20:00";
    public string ScheduleDays { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";
    public string ScheduleDate { get; set; } = "";
    public string ActiveFromDate { get; set; } = "";
    public string ActiveUntilDate { get; set; } = "";
    public string ExcludedDates { get; set; } = "";
    public string BlackoutRanges { get; set; } = "";
    public string MissedRunBehavior { get; set; } = "SameDay";
    public int CatchUpGraceMinutes { get; set; } = 30;
    public string DependencyRuleId { get; set; } = "";
    public string DependencyRequiredStatus { get; set; } = "Erfolgreich";
    public int RetryCount { get; set; }
    public int RetryDelaySeconds { get; set; } = 5;
    public string FailureRuleId { get; set; } = "";
    public string WorkflowGroup { get; set; } = "";
    public int WorkflowOrder { get; set; }
    public bool StartWorkflowGroup { get; set; }
    public string WorkflowFailureMode { get; set; } = "Stop";
    public string RollbackRuleId { get; set; } = "";
    public double DesignerX { get; set; }
    public double DesignerY { get; set; }
    public string DesignerNodeType { get; set; } = "Action";
    public string LastScheduledRunDate { get; set; } = "";
    public string LastRunAt { get; set; } = "";
    public string LastRunStatus { get; set; } = "Noch nie";
    public int SuccessfulRuns { get; set; }
    public int FailedRuns { get; set; }
    public int SkippedRuns { get; set; }
}

public sealed class StreamDeckSettings
{
    public bool Enabled { get; set; } = true;
    public bool AutoInstallProfile { get; set; } = true;
}

public sealed class DashboardSettings
{
    public bool ShowServiceStatus { get; set; } = true;
    public bool ShowStreamControls { get; set; } = true;
    public bool ShowLivePanels { get; set; } = true;
    public bool ShowQuickServices { get; set; } = true;
    public bool ShowWorkflowRail { get; set; } = true;
    public bool ShowAdvancedTools { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool ShowStreamHistory { get; set; } = true;
    public string DashboardStatistic { get; set; } = "ViewerCount";
    public List<DashboardSceneButtonSettings> SceneButtons { get; set; } = [];

    public List<string> ModuleOrder { get; set; } =
    [
        "ConnectionStatus",
        "Community",
        "ObsSceneControl",
        "StreamEnd",
        "StreamControl",
        "QuickServices",
        "SpotifyPlayer",
        "TwitchChat",
        "Workflow",
        "Preflight",
        "Scenes",
        "RaidControl",
        "RaidAssistant",
        "Notifications",
        "TwitchEvents",
        "Automation",
        "LiveEvents",
        "SystemResources",
        "StreamHistory",
        "AudioMixer",
        "TwitchUsers",
        "StreamDeckRemote",
        "AdvancedShortcuts",
        "WorkflowStatus",
    ];

    public Dictionary<string, string> ModuleZones { get; set; } =
        new(StringComparer.Ordinal)
        {
            ["StreamStatus"] = "Center",
            ["ObsStatus"] = "Left",
            ["TwitchStatus"] = "Left",
            ["SpotifyStatus"] = "Right",
            ["StreamerBotStatus"] = "Right",
            ["AlertsStatus"] = "Right",
            ["StreamControl"] = "Center",
            ["ObsSceneControl"] = "Center",
            ["RaidControl"] = "Right",
            ["TwitchChat"] = "Center",
            ["TwitchUsers"] = "Right",
            ["TwitchEvents"] = "Right",
            ["SpotifyPlayer"] = "Right",
            ["QuickObs"] = "Left",
            ["QuickTwitch"] = "Left",
            ["QuickSpotify"] = "Left",
            ["QuickStreamerBot"] = "Left",
            ["QuickAlerts"] = "Left",
            ["QuickOverlay"] = "Left",
            ["Community"] = "Center",
            ["SystemResources"] = "Left",
            ["Workflow"] = "Center",
            ["WorkflowStatus"] = "Center",
            ["Preflight"] = "Left",
            ["Scenes"] = "Left",
            ["AudioMixer"] = "Left",
            ["RaidAssistant"] = "Right",
            ["Notifications"] = "Right",
            ["StreamDeckRemote"] = "Right",
            ["AdvancedShortcuts"] = "Right",
            ["StreamHistory"] = "Center",
        };

    public List<string> HiddenModules { get; set; } = [];

    public bool AutoFocusModeOnStreamStart { get; set; }
    public bool AutoExitFocusModeOnStreamEnd { get; set; } = true;
    public string ObsScenePreviewSize { get; set; } = "Standard";

    /// <summary>Ob der Bereich „Streamende &amp; Raid“ unter Schnellzugriff ausgeklappt ist.</summary>
    public bool StreamEndExpanded { get; set; }

    public Dictionary<string, string> ModuleSizes { get; set; } =
        new(StringComparer.Ordinal)
        {
            ["StreamStatus"] = "Standard",
            ["ObsStatus"] = "Standard",
            ["TwitchStatus"] = "Standard",
            ["SpotifyStatus"] = "Standard",
            ["StreamerBotStatus"] = "Standard",
            ["AlertsStatus"] = "Standard",
            ["StreamControl"] = "Standard",
            ["ObsSceneControl"] = "Standard",
            ["RaidControl"] = "Standard",
            ["TwitchChat"] = "Standard",
            ["TwitchUsers"] = "Standard",
            ["TwitchEvents"] = "Standard",
            ["SpotifyPlayer"] = "Standard",
            ["QuickObs"] = "Standard",
            ["QuickTwitch"] = "Standard",
            ["QuickSpotify"] = "Standard",
            ["QuickStreamerBot"] = "Standard",
            ["QuickAlerts"] = "Standard",
            ["QuickOverlay"] = "Standard",
            ["Community"] = "Standard",
            ["SystemResources"] = "Standard",
            ["Workflow"] = "Standard",
            ["WorkflowStatus"] = "Standard",
            ["Preflight"] = "Standard",
            ["Scenes"] = "Standard",
            ["AudioMixer"] = "Standard",
            ["RaidAssistant"] = "Standard",
            ["Notifications"] = "Standard",
            ["StreamDeckRemote"] = "Standard",
            ["AdvancedShortcuts"] = "Standard",
            ["StreamHistory"] = "Standard",
        };

    public Dictionary<string, double> ModuleWidths { get; set; } =
        new(StringComparer.Ordinal)
        {
            ["LiveEvents"] = 400,
            ["Automation"] = 400,
            ["ConnectionStatus"] = 1220,
            ["Community"] = 470,
            ["ObsSceneControl"] = 360,
            ["StreamControl"] = 360,
            ["QuickServices"] = 550,
            ["SpotifyPlayer"] = 420,
            ["TwitchChat"] = 420,
            ["Workflow"] = 1220,
            ["Preflight"] = 380,
            ["Scenes"] = 400,
            ["RaidControl"] = 380,
            ["RaidAssistant"] = 400,
            ["Notifications"] = 400,
            ["TwitchEvents"] = 400,
            ["SystemResources"] = 550,
            ["StreamHistory"] = 1220,
            ["AudioMixer"] = 500,
            ["TwitchUsers"] = 360,
            ["StreamDeckRemote"] = 400,
            ["AdvancedShortcuts"] = 400,
            ["WorkflowStatus"] = 420,
        };

    public Dictionary<string, double> ModuleHeights { get; set; } =
        new(StringComparer.Ordinal)
        {
            ["ConnectionStatus"] = 86,
            ["Community"] = 130,
            ["ObsSceneControl"] = 350,
            ["StreamControl"] = 350,
            ["QuickServices"] = 330,
            ["SpotifyPlayer"] = 330,
            ["TwitchChat"] = 330,
            ["Workflow"] = 190,
            ["Preflight"] = 300,
            ["Scenes"] = 300,
            ["RaidControl"] = 300,
            ["RaidAssistant"] = 300,
            ["Notifications"] = 340,
            ["TwitchEvents"] = 280,
            ["SystemResources"] = 280,
            ["StreamHistory"] = 360,
            ["AudioMixer"] = 330,
            ["TwitchUsers"] = 270,
            ["StreamDeckRemote"] = 240,
            ["AdvancedShortcuts"] = 240,
            ["WorkflowStatus"] = 280,
        };
}

public sealed class DashboardSceneButtonSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string SceneName { get; set; } = "";
    /// <summary>Emoji | Glyph | Image</summary>
    public string IconKind { get; set; } = "Emoji";
    /// <summary>Emoji-Zeichen, Segoe-MDL2-Glyph oder Bildpfad.</summary>
    public string IconValue { get; set; } = "🎬";
}

public sealed class UpdateSettings
{
    public string Channel { get; set; } = "Alpha";
    public bool AutoCheck { get; set; } = true;
    public bool BackupBeforeUpdate { get; set; } = true;
}
