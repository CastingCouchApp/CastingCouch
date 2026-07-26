namespace CreatorControlSuite.Core.Setup;

public sealed class FirstRunState
{
    public bool Completed { get; set; }
    public int CompletedVersion { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed record FirstRunSummary(
    string DisplayName,
    string TwitchChannel,
    string ObsHost,
    int ObsPort,
    string StartScene,
    string LiveScene,
    string PauseScene,
    string EndScene,
    string OverlayRoot);
