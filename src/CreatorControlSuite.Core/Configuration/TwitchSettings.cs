namespace CreatorControlSuite.Core.Configuration;

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

    /// <summary>Anzeige des Twitch-Chats in Dashboard/Dienste: Built-in (EventSub/Helix) oder eingebetteter Web-Popout.</summary>
    public TwitchChatUiMode ChatUiMode { get; set; } = TwitchChatUiMode.BuiltIn;

    public bool EnableEventSub { get; set; } = true;
    public bool UseDeviceCodeFlow { get; set; } = true;

    /// <summary>Chatter-Listen-Intervall in Sekunden, wenn Zuschauer unter der Schwelle liegen.</summary>
    public int ChattersRefreshSecondsLow { get; set; } = 10;

    /// <summary>Chatter-Listen-Intervall in Sekunden, wenn Zuschauer die Schwelle erreichen oder überschreiten.</summary>
    public int ChattersRefreshSecondsHigh { get; set; } = 60;

    /// <summary>Zuschauerzahl, ab der die langsamere Chatter-Listen-Aktualisierung gilt.</summary>
    public int ChattersRefreshViewerThreshold { get; set; } = 50;

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

