namespace CreatorControlSuite.Core.Configuration;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

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
    public WorkflowSettings Workflow { get; set; } = new();
    public StreamDeckSettings StreamDeck { get; set; } = new();
    public DashboardSettings Dashboard { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
}
