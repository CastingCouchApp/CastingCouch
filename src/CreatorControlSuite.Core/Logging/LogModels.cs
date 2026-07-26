namespace CreatorControlSuite.Core.Logging;

public enum AppLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    AppLogLevel Level,
    string Category,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, string> Properties);
