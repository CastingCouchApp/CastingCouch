namespace CreatorControlSuite.Modules.Workflow.Models;

public enum StreamPhase
{
    Idle,
    Preparing,
    Countdown,
    Live,
    Paused,
    Ending,
    Completed,
    Error
}

public sealed record WorkflowState(
    StreamPhase Phase,
    DateTimeOffset? SessionStartedAt,
    DateTimeOffset? LiveStartedAt,
    DateTimeOffset? EndedAt,
    int CountdownRemainingSeconds,
    string CurrentScene,
    string Detail);

public sealed record ViewerSample(
    DateTimeOffset Timestamp,
    int ViewerCount);

public sealed class StreamSessionStats
{
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int FollowersAtStart { get; set; }
    public int FollowersAtEnd { get; set; }
    public int ChatMessages { get; set; }
    public int AlertsPlayed { get; set; }
    public int NewSubscriptions { get; set; }
    public int GiftSubscriptions { get; set; }
    public int BitsCheered { get; set; }
    public int IncomingRaids { get; set; }
    public List<ViewerSample> ViewerSamples { get; } = [];

    public int FollowersGained =>
        Math.Max(0, FollowersAtEnd - FollowersAtStart);

    public int PeakViewers =>
        ViewerSamples.Count == 0
            ? 0
            : ViewerSamples.Max(sample => sample.ViewerCount);

    public double AverageViewers =>
        ViewerSamples.Count == 0
            ? 0
            : ViewerSamples.Average(sample => sample.ViewerCount);

    public long StreamTimeSeconds =>
        StartedAt is null
            ? 0
            : (long)Math.Max(
                0,
                ((EndedAt ?? DateTimeOffset.Now) - StartedAt.Value)
                .TotalSeconds);
}
