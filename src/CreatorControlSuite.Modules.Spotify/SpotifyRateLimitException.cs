namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyRateLimitException : Exception
{
    public SpotifyRateLimitException(TimeSpan retryAfter, string? responseBody = null)
        : base(string.IsNullOrWhiteSpace(responseBody)
            ? "Spotify API-Limit erreicht."
            : $"Spotify API-Limit erreicht: {responseBody}")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}
