namespace CreatorControlSuite.Core.Configuration;

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

