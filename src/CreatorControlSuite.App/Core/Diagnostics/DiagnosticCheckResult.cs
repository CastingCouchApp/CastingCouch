namespace CreatorControlSuite.App.Core.Diagnostics;

public sealed record DiagnosticCheckResult(
    string Name,
    DiagnosticStatus Status,
    string Message,
    string? SuggestedAction = null);

public enum DiagnosticStatus
{
    Ready,
    Warning,
    Error
}
