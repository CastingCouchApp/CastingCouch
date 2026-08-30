/** AppSettings 1:1 zu ccs-core serde (`rename_all = "PascalCase"`).
 * Flatten-Extra-Keys (WPF) sind zur Laufzeit am Objekt; Save nutzt `applyEditedSettings`, damit sie nicht verloren gehen.
 * Keine Index-Signatur auf Form-Structs — sonst werden TanStack-Form-Pfade zu `unknown`.
 */

export const CURRENT_SCHEMA_VERSION = 2;

export type ProductSettings = {
  ProductName: string;
  Version: string;
  UpdateChannel: string;
};

export type GeneralSettings = {
  Language: string;
  ThemeId: string;
  TitleBarWidgetCardsEnabled: boolean;
  TitleBarHiddenWidgets: string[];
  DataRoot: string;
  BackupRoot: string;
  OverlayManifestPath: string;
  StartWithWindows: boolean;
  MinimizeToTray: boolean;
  ConnectionWatchdogEnabled: boolean;
  ConnectionWatchdogSeconds: number;
  ReconnectObs: boolean;
  ReconnectTwitch: boolean;
  ReconnectSpotify: boolean;
  ReconnectYouTubeMusic: boolean;
  ReconnectStreamerBot: boolean;
};

export type BrandingSettings = {
  DisplayName: string;
  ChannelName: string;
  AccentColor: string;
  LogoPath: string;
};

export type ObsSettings = {
  Host: string;
  Port: number;
  AutoConnect: boolean;
  ConnectOnPrepare: boolean;
  ExecutablePath: string;
  StartScene: string;
  LiveScene: string;
  PauseScene: string;
  EndScene: string;
  GoalOverlayScene: string;
  MicrophoneSource: string;
  DesktopAudioSource: string;
  MusicSource: string;
  CameraSource: string;
  AudioProfiles: unknown[];
};

export type TwitchSettings = {
  ClientId: string;
  ChannelName: string;
  AutoConnect: boolean;
  ConnectOnPrepare: boolean;
  CreatorDashboardUrl: string;
  EnableChat: boolean;
  ChatUiMode: string;
  EnableEventSub: boolean;
  UseDeviceCodeFlow: boolean;
  Scopes: string[];
};

export type SpotifySettings = {
  ClientId: string;
  RedirectUri: string;
  AutoConnect: boolean;
  Scopes: string[];
};

export type MusicPlayerSettings = {
  Source: string;
};

export type YouTubeMusicSettings = Record<string, unknown>;
export type StreamerBotSettings = Record<string, unknown>;
export type WorkflowSettings = Record<string, unknown>;
export type StreamDeckSettings = Record<string, unknown>;
export type DashboardSettings = Record<string, unknown>;

export type AlertDefinitionSettings = {
  Type: string;
  Enabled: boolean;
  TextTemplate: string;
  MediaPath: string;
  SoundPath: string;
  DurationSeconds: number;
  Priority: number;
  FontFace: string;
  FontSize: number;
  FontColor: string;
  Animation: string;
  X: number;
  Y: number;
  Width: number;
  Height: number;
  VolumePercent: number;
  SoundStartSeconds: number;
  SoundEndSeconds: number;
  AudioOutputDeviceId: string;
};

export type AlertSettings = {
  Enabled: boolean;
  ObsSceneName: string;
  ObsMediaSourceName: string;
  ObsTextSourceName: string;
  AudioOutputDeviceId: string;
  QueueCapacity: number;
  InterAlertDelayMilliseconds: number;
  StopPreviousMediaBeforeNext: boolean;
  AutoCreateObsSources: boolean;
  Definitions: Record<string, AlertDefinitionSettings>;
};

export type OverlayCanvasSettings = {
  Id: string;
  Name: string;
};

export type OverlayChatSettings = {
  Enabled: boolean;
  EnableBttv: boolean;
  EnableFfz: boolean;
  EnableSevenTv: boolean;
  ShowTwitchEvents: boolean;
  MaxBufferedMessages: number;
};

export type OverlaySettings = {
  RootPath: string;
  DataFileName: string;
  DataFilePath: string;
  AdditionalDataRoots: string[];
  Instances: unknown[];
  Canvases: OverlayCanvasSettings[];
  SelectedCanvasId: string;
  WebServerPort: number;
  Chat: OverlayChatSettings;
};

export type UpdateSettings = {
  Channel: string;
  CheckOnStartup: boolean;
};

export type SidecarSettings = {
  Enabled: boolean;
  Port: number;
  BinaryPath: string;
};

export type AppSettings = {
  SchemaVersion: number;
  AdditionalScenes: string[];
  Product: ProductSettings;
  General: GeneralSettings;
  Branding: BrandingSettings;
  Obs: ObsSettings;
  Twitch: TwitchSettings;
  Spotify: SpotifySettings;
  MusicPlayer: MusicPlayerSettings;
  YouTubeMusic: YouTubeMusicSettings;
  StreamerBot: StreamerBotSettings;
  Alerts: AlertSettings;
  Overlay: OverlaySettings;
  Workflow: WorkflowSettings;
  StreamDeck: StreamDeckSettings;
  Dashboard: DashboardSettings;
  Updates: UpdateSettings;
  Sidecar: SidecarSettings;
};

export const THEME_CATALOG: { id: string; label: string }[] = [
  { id: "classic", label: "Classic" },
  { id: "comic-sans-extravaganza", label: "Comic Sans Extravaganza" },
  { id: "pink-cage-flair", label: "Pink Cage Flair" },
  { id: "vespucci-heights", label: "Vespucci Heights" },
  { id: "vanilla-unicorn-lounge", label: "Vanilla Unicorn Lounge" },
  { id: "neon-night-market", label: "Neon Night Market" },
  { id: "terminal-green-override", label: "Terminal Green Override" },
  { id: "blood-moon-broadcast", label: "Blood Moon Broadcast" },
  { id: "pastel-lofi-cafe", label: "Pastel Lo-Fi Café" },
  { id: "gold-rush-studio", label: "Gold Rush Studio" },
  { id: "arctic-glass-lab", label: "Arctic Glass Lab" },
  { id: "biomilchs-bubatz-cantina", label: "biomilchs Bubatz Cantina" },
  { id: "fruppis-landadel-kanzlei", label: "fruppis Landadel Kanzlei" },
];

const DEFAULT_TWITCH_SCOPES = [
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
  "channel:manage:predictions",
];

const DEFAULT_SPOTIFY_SCOPES = ["user-read-playback-state", "user-read-currently-playing"];

function defaultAlertDefinition(
  type: string,
  textTemplate: string,
  durationSeconds: number,
  priority: number,
  animation = "Fade",
): AlertDefinitionSettings {
  return {
    Type: type,
    Enabled: true,
    TextTemplate: textTemplate,
    MediaPath: "",
    SoundPath: "",
    DurationSeconds: durationSeconds,
    Priority: priority,
    FontFace: "Segoe UI",
    FontSize: 44,
    FontColor: "#FFFFFF",
    Animation: animation,
    X: 510,
    Y: 690,
    Width: 900,
    Height: 260,
    VolumePercent: 100,
    SoundStartSeconds: 0,
    SoundEndSeconds: 0,
    AudioOutputDeviceId: "",
  };
}

export function defaultAlertDefinitions(): Record<string, AlertDefinitionSettings> {
  return {
    Follow: defaultAlertDefinition("Follow", "{user} folgt jetzt!", 8, 100),
    Sub: defaultAlertDefinition("Sub", "{user} hat abonniert!", 9, 80),
    ReSub: defaultAlertDefinition("ReSub", "{user} ist seit {months} Monaten dabei!", 9, 75),
    GiftSub: defaultAlertDefinition("GiftSub", "{user} verschenkt {count} Subs!", 10, 70),
    Cheer: defaultAlertDefinition("Cheer", "{user} cheeret {bits} Bits!", 9, 85),
    Raid: defaultAlertDefinition("Raid", "Raid von {user} mit {viewers} Zuschauern!", 12, 10, "Slide"),
  };
}

/** Defaults analog `AppSettings::default()` in ccs-core. */
export function defaultAppSettings(): AppSettings {
  return {
    SchemaVersion: CURRENT_SCHEMA_VERSION,
    AdditionalScenes: [],
    Product: {
      ProductName: "CastingCouch",
      Version: "8.0.0-beta1",
      UpdateChannel: "Alpha",
    },
    General: {
      Language: "de-DE",
      ThemeId: "classic",
      TitleBarWidgetCardsEnabled: false,
      TitleBarHiddenWidgets: [],
      DataRoot: "",
      BackupRoot: "",
      OverlayManifestPath: "",
      StartWithWindows: false,
      MinimizeToTray: false,
      ConnectionWatchdogEnabled: true,
      ConnectionWatchdogSeconds: 15,
      ReconnectObs: true,
      ReconnectTwitch: true,
      ReconnectSpotify: true,
      ReconnectYouTubeMusic: true,
      ReconnectStreamerBot: true,
    },
    Branding: {
      DisplayName: "Mein Stream",
      ChannelName: "",
      AccentColor: "#FF8C00",
      LogoPath: "",
    },
    Obs: {
      Host: "127.0.0.1",
      Port: 4455,
      AutoConnect: true,
      ConnectOnPrepare: true,
      ExecutablePath: "",
      StartScene: "Start",
      LiveScene: "Game",
      PauseScene: "Pause",
      EndScene: "Ende",
      GoalOverlayScene: "",
      MicrophoneSource: "",
      DesktopAudioSource: "",
      MusicSource: "",
      CameraSource: "",
      AudioProfiles: [],
    },
    Twitch: {
      ClientId: "",
      ChannelName: "",
      AutoConnect: true,
      ConnectOnPrepare: true,
      CreatorDashboardUrl: "",
      EnableChat: true,
      ChatUiMode: "BuiltIn",
      EnableEventSub: true,
      UseDeviceCodeFlow: true,
      Scopes: [...DEFAULT_TWITCH_SCOPES],
    },
    Spotify: {
      ClientId: "",
      RedirectUri: "http://127.0.0.1:43821/callback/",
      AutoConnect: true,
      Scopes: [...DEFAULT_SPOTIFY_SCOPES],
    },
    MusicPlayer: { Source: "" },
    YouTubeMusic: {},
    StreamerBot: {},
    Alerts: {
      Enabled: true,
      ObsSceneName: "_alerts",
      ObsMediaSourceName: "ccs_alert_media",
      ObsTextSourceName: "ccs_alert_text",
      AudioOutputDeviceId: "",
      QueueCapacity: 250,
      InterAlertDelayMilliseconds: 350,
      StopPreviousMediaBeforeNext: true,
      AutoCreateObsSources: false,
      Definitions: defaultAlertDefinitions(),
    },
    Overlay: {
      RootPath: "",
      DataFileName: "overlay-data.json",
      DataFilePath: "",
      AdditionalDataRoots: [],
      Instances: [],
      Canvases: [{ Id: "default", Name: "Canvas" }],
      SelectedCanvasId: "default",
      WebServerPort: 8765,
      Chat: {
        Enabled: true,
        EnableBttv: true,
        EnableFfz: true,
        EnableSevenTv: true,
        ShowTwitchEvents: true,
        MaxBufferedMessages: 100,
      },
    },
    Workflow: {},
    StreamDeck: {},
    Dashboard: {},
    Updates: {
      Channel: "Alpha",
      CheckOnStartup: true,
    },
    Sidecar: {
      Enabled: false,
      Port: 18765,
      BinaryPath: "",
    },
  };
}

export function cloneSettings(settings: AppSettings): AppSettings {
  return structuredClone(settings);
}

/** Copies only Settings-UI fields onto a clone of `base` so unedited/WPF extra keys survive. */
export function applyEditedSettings(base: AppSettings, form: AppSettings): AppSettings {
  const next = cloneSettings(base);
  const g = form.General;
  next.General.Language = g.Language;
  next.General.ThemeId = g.ThemeId;
  next.General.TitleBarWidgetCardsEnabled = g.TitleBarWidgetCardsEnabled;
  next.General.StartWithWindows = g.StartWithWindows;
  next.General.MinimizeToTray = g.MinimizeToTray;
  next.General.ConnectionWatchdogEnabled = g.ConnectionWatchdogEnabled;
  next.General.ConnectionWatchdogSeconds = g.ConnectionWatchdogSeconds;
  next.General.ReconnectObs = g.ReconnectObs;
  next.General.ReconnectTwitch = g.ReconnectTwitch;
  next.General.ReconnectSpotify = g.ReconnectSpotify;
  next.General.ReconnectYouTubeMusic = g.ReconnectYouTubeMusic;
  next.General.ReconnectStreamerBot = g.ReconnectStreamerBot;

  const o = form.Obs;
  next.Obs.Host = o.Host;
  next.Obs.Port = o.Port;
  next.Obs.AutoConnect = o.AutoConnect;
  next.Obs.ConnectOnPrepare = o.ConnectOnPrepare;
  next.Obs.ExecutablePath = o.ExecutablePath;
  next.Obs.StartScene = o.StartScene;
  next.Obs.LiveScene = o.LiveScene;
  next.Obs.PauseScene = o.PauseScene;
  next.Obs.EndScene = o.EndScene;

  const t = form.Twitch;
  next.Twitch.ClientId = t.ClientId;
  next.Twitch.ChannelName = t.ChannelName;
  next.Twitch.CreatorDashboardUrl = t.CreatorDashboardUrl;
  next.Twitch.AutoConnect = t.AutoConnect;
  next.Twitch.ConnectOnPrepare = t.ConnectOnPrepare;
  next.Twitch.EnableChat = t.EnableChat;
  next.Twitch.EnableEventSub = t.EnableEventSub;

  next.Spotify.ClientId = form.Spotify.ClientId;
  next.Spotify.RedirectUri = form.Spotify.RedirectUri;
  next.Spotify.AutoConnect = form.Spotify.AutoConnect;

  next.Overlay.WebServerPort = form.Overlay.WebServerPort;
  next.Overlay.SelectedCanvasId = form.Overlay.SelectedCanvasId;
  next.Overlay.Chat.Enabled = form.Overlay.Chat.Enabled;
  next.Overlay.Chat.EnableBttv = form.Overlay.Chat.EnableBttv;
  next.Overlay.Chat.EnableFfz = form.Overlay.Chat.EnableFfz;
  next.Overlay.Chat.EnableSevenTv = form.Overlay.Chat.EnableSevenTv;
  next.Overlay.Chat.ShowTwitchEvents = form.Overlay.Chat.ShowTwitchEvents;

  next.Branding.DisplayName = form.Branding.DisplayName;
  next.Branding.ChannelName = form.Branding.ChannelName;
  next.Branding.AccentColor = form.Branding.AccentColor;
  next.Branding.LogoPath = form.Branding.LogoPath;

  return next;
}

export function applyThemeId(themeId: string | undefined | null): void {
  if (typeof document === "undefined") {
    return;
  }
  const id = themeId?.trim() || "classic";
  document.documentElement.dataset.theme = id;
}
