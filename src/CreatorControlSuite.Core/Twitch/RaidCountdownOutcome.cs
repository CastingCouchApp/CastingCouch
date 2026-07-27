namespace CreatorControlSuite.Core.Twitch;

/// <summary>
/// How a local raid-countdown wait ended.
/// </summary>
public enum RaidCountdownOutcome
{
    Completed,
    Skipped,
    Cancelled
}

/// <summary>
/// Pure helpers for the local raid-countdown skip/cancel distinction.
/// </summary>
public static class RaidCountdownPolicy
{
    /// <summary>
    /// Maps cancellation cause to an outcome. Skip means the raid already runs on Twitch
    /// and the app should continue the stream-end flow without waiting further.
    /// </summary>
    public static RaidCountdownOutcome DecideAfterCancellation(bool skipRequested) =>
        skipRequested ? RaidCountdownOutcome.Skipped : RaidCountdownOutcome.Cancelled;

    public static bool IsSuccessful(RaidCountdownOutcome outcome) =>
        outcome is RaidCountdownOutcome.Completed or RaidCountdownOutcome.Skipped;
}
