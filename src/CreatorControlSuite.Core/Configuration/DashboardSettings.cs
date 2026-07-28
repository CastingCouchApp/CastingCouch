namespace CreatorControlSuite.Core.Configuration;

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

