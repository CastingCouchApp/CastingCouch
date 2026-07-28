namespace CreatorControlSuite.Core.Configuration;

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

