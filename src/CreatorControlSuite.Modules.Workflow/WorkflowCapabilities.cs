namespace CreatorControlSuite.Modules.Workflow;

public interface IWorkflowObsCapability
{
    bool IsConnected { get; }
    Task SetSceneAsync(string sceneName, CancellationToken cancellationToken);
    Task<bool> IsStreamActiveAsync(CancellationToken cancellationToken);
    Task StartStreamAsync(CancellationToken cancellationToken);
    Task StopStreamAsync(CancellationToken cancellationToken);
}

public interface IWorkflowMusicCapability
{
    Task FadeToAsync(
        int targetVolumePercent,
        TimeSpan duration,
        bool pauseAfterFade,
        CancellationToken cancellationToken);
}

public interface IWorkflowAlertCapability
{
    Task StopAndClearAsync(CancellationToken cancellationToken);
}

public interface IWorkflowOverlayCapability
{
    Task ClearChatAsync(CancellationToken cancellationToken);

    Task UpdateAsync(
        Action<WorkflowOverlayData> update,
        CancellationToken cancellationToken);
}

public sealed class WorkflowOverlayData
{
    public WorkflowOverlayStream Stream { get; set; } = new();
    public WorkflowOverlayStats Stats { get; set; } = new();
    public WorkflowOverlayCountdown Countdown { get; set; } = new();
    public WorkflowOverlayTwitch Twitch { get; set; } = new();
}

public sealed class WorkflowOverlayStream
{
    public bool IsLive { get; set; }
    public string Phase { get; set; } = "Idle";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long ElapsedSeconds { get; set; }
    public int ViewerCount { get; set; }
    public string CurrentScene { get; set; } = "";
}

public sealed class WorkflowOverlayStats
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

public sealed class WorkflowOverlayCountdown
{
    public bool IsRunning { get; set; }
    public int RemainingSeconds { get; set; }
    public int TotalSeconds { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string Label { get; set; } = "Countdown";
    public string Mode { get; set; } = "manual";
}

public sealed class WorkflowOverlayTwitch
{
    public int Followers { get; set; }
}
