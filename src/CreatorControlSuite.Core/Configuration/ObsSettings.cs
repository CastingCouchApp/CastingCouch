namespace CreatorControlSuite.Core.Configuration;

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

