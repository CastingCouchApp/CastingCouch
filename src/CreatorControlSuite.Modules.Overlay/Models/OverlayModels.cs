namespace CreatorControlSuite.Modules.Overlay.Models;

public sealed class OverlayData
{
    public OverlayStreamState Stream { get; set; } = new();
    public OverlayTwitchState Twitch { get; set; } = new();
    public OverlaySpotifyState Spotify { get; set; } = new();
    public OverlayObsState Obs { get; set; } = new();
    public OverlayAlertState Alerts { get; set; } = new();
    public OverlaySessionStats Stats { get; set; } = new();
    public OverlayBranding Branding { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OverlayStreamState
{
    public bool IsLive { get; set; }
    public string Phase { get; set; } = "Idle";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long ElapsedSeconds { get; set; }
    public int ViewerCount { get; set; }
    public string CurrentScene { get; set; } = "";
}

public sealed class OverlayTwitchState
{
    public string ChannelName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int Followers { get; set; }
    public int FollowerGoal { get; set; } = 200;
    public string LastFollower { get; set; } = "";
    public string LastEvent { get; set; } = "";
    public OverlayGoalState FollowerGoalState { get; set; } = new();
    public OverlayGoalState SubGoalState { get; set; } = new();
    public OverlayGoalState DonationGoalState { get; set; } = new();
}

public sealed class OverlaySpotifyState
{
    public bool Connected { get; set; }
    public bool IsPlaying { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string Cover { get; set; } = "";
    public bool ShowInOverlay { get; set; } = true;
    public int ProgressMs { get; set; }
    public int DurationMs { get; set; }
    public string StatusText { get; set; } = "Nicht verbunden";
    public bool ShowTitle { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowAlbumCover { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool HideWhenPaused { get; set; } = false;
    public bool HideWhenMuted { get; set; } = true;
}

public sealed class OverlayGoalState
{
    public string Title { get; set; } = "";
    public double Current { get; set; }
    public double Target { get; set; }
    public string FontFace { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 36;
    public string Currency { get; set; } = "";
}

public sealed class OverlayObsState
{
    public bool Connected { get; set; }
    public string CurrentScene { get; set; } = "";
    public bool MicrophoneMuted { get; set; }
    public bool DesktopAudioMuted { get; set; }
}

public sealed class OverlayAlertState
{
    public bool IsRunning { get; set; }
    public string CurrentType { get; set; } = "";
    public int QueueLength { get; set; }
}

public sealed class OverlaySessionStats
{
    public int FollowersGained { get; set; }
    public int PeakViewers { get; set; }
    public double AverageViewers { get; set; }
    public long StreamTimeSeconds { get; set; }
    public int ChatMessages { get; set; }
    public int AlertsPlayed { get; set; }
    public int NewSubscriptions { get; set; }
    public int GiftSubscriptions { get; set; }
    public int BitsCheered { get; set; }
    public int IncomingRaids { get; set; }
}

public sealed class OverlayBranding
{
    public string DisplayName { get; set; } = "Mein Stream";
    public string ChannelName { get; set; } = "";
    public string AccentColor { get; set; } = "#FF8C00";
    public string LogoPath { get; set; } = "";
}
