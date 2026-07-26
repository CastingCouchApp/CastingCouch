namespace CreatorControlSuite.Core.Diagnostics;

public sealed record CrashReport(
    string Id,
    DateTimeOffset Timestamp,
    string ApplicationVersion,
    string OperatingSystem,
    string RuntimeVersion,
    string ProcessArchitecture,
    string ExceptionType,
    string Message,
    string StackTrace,
    string ExceptionText,
    IReadOnlyDictionary<string, string> Context);
