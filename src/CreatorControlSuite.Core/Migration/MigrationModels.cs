namespace CreatorControlSuite.Core.Migration;

public sealed record MigrationCandidate(
    string SourceType,
    string SourcePath,
    string DisplayName,
    IReadOnlyList<string> DetectedItems);

public sealed record MigrationResult(
    bool Success,
    string SourcePath,
    IReadOnlyList<string> ImportedItems,
    IReadOnlyList<string> Warnings,
    string Detail);
