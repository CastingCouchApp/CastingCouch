namespace CreatorControlSuite.Core.Diagnostics;

public sealed record SupportPackageOptions(bool IncludeSettings, bool IncludeLogs, bool IncludeCrashReports, bool IncludeDiagnostics, bool IncludeProfiles, bool IncludeOverlayData);
public sealed record SupportPackageResult(string PackagePath, DateTimeOffset CreatedAt, IReadOnlyList<string> IncludedItems, IReadOnlyList<string> Warnings);
