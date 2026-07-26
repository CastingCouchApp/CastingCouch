namespace CreatorControlSuite.Core.Updates;

public sealed record UpdatePackage(
    string Version,
    string Channel,
    Uri DownloadUri,
    string Sha256,
    long SizeBytes,
    string ReleaseNotes,
    bool Mandatory,
    SignedUpdateManifest? Manifest = null);

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    UpdatePackage? Package,
    string Detail);

public sealed record UpdateBackup(
    string Id,
    string Version,
    string Path,
    DateTimeOffset CreatedAt,
    long SizeBytes);
