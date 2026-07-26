namespace CreatorControlSuite.Core.Licensing;
public sealed record LicenseServerActivationRequest(string ProductId,string LicenseKey,string InstallationId,string AppVersion);
public sealed record LicenseServerActivationResponse(bool Success,string Detail,string? LicenseDocumentJson,string? ActivationId);
public sealed record LicenseServerStatusResponse(bool Success,string Detail,bool Revoked,DateTimeOffset? CheckedAt);
public sealed record LicenseServerDeactivationRequest(string ProductId,string ActivationId,string InstallationId);
