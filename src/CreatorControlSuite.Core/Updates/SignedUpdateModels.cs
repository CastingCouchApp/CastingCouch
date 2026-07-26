namespace CreatorControlSuite.Core.Updates;

public sealed record SignedUpdateManifest(
    string ProductId,
    string Version,
    string Channel,
    string PackageFileName,
    string PackageSha256,
    long PackageSizeBytes,
    DateTimeOffset PublishedAt,
    string MinimumVersion,
    string ReleaseNotes,
    string Signature);
