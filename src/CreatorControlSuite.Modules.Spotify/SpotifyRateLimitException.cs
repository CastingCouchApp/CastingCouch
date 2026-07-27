namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyRateLimitException(TimeSpan retryAfter, string? responseBody = null) : Exception(string.IsNullOrWhiteSpace(responseBody)
            ? "Spotify API-Limit erreicht."
            : $"Spotify API-Limit erreicht: {responseBody}")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
