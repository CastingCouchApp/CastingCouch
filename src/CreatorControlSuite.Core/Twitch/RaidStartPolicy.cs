namespace CreatorControlSuite.Core.Twitch;

/// <summary>
/// Pure decision helpers for resilient auto-raid after the end scene.
/// </summary>
public static class RaidStartPolicy
{
    public const int DefaultTimeoutSeconds = 120;
    public const int MinTimeoutSeconds = 15;
    public const int MaxTimeoutSeconds = 600;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public static int ClampTimeoutSeconds(int seconds) =>
        Math.Clamp(
            seconds <= 0 ? DefaultTimeoutSeconds : seconds,
            MinTimeoutSeconds,
            MaxTimeoutSeconds);

    public static TimeSpan GetRetryDelay(int attemptIndex)
    {
        // 5s, 5s, 8s, 12s, 15s (capped)
        int seconds = attemptIndex switch
        {
            <= 1 => 5,
            2 => 8,
            3 => 12,
            _ => 15
        };
        return TimeSpan.FromSeconds(seconds);
    }

    public static RaidStartDecision DecideAfterStatus(
        bool targetFound,
        bool isOnline)
    {
        if (!targetFound)
        {
            return RaidStartDecision.KeepPolling;
        }

        return isOnline
            ? RaidStartDecision.AttemptStart
            : RaidStartDecision.KeepPolling;
    }

    public static RaidStartDecision DecideAfterStartError(Exception exception)
    {
        if (IsPermanentRaidError(exception))
        {
            return RaidStartDecision.GiveUp;
        }

        return RaidStartDecision.RetryTransient;
    }

    public static bool IsTransientRaidError(Exception exception)
    {
        if (exception is OperationCanceledException or TaskCanceledException)
        {
            return false;
        }

        if (IsPermanentRaidError(exception))
        {
            return false;
        }

        string message = exception.Message;
        if (ContainsAny(message,
                "503", "502", "500", "429", "timeout", "timed out",
                "temporar", "network", "connection", "noch nicht"))
        {
            return true;
        }

        // Unknown Helix / network failures: retry until timeout.
        return true;
    }

    public static bool IsPermanentRaidError(Exception exception)
    {
        string message = exception.Message;
        return ContainsAny(
            message,
            "eigene kanal",
            "nicht gefunden",
            "nicht verbunden",
            "nicht konfiguriert",
            "403",
            "401");
    }

    private static bool ContainsAny(string message, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (message.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public enum RaidStartDecision
{
    KeepPolling,
    AttemptStart,
    RetryTransient,
    GiveUp
}
