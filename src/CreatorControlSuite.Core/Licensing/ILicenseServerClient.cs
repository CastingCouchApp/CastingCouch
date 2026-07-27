namespace CreatorControlSuite.Core.Licensing;

public interface ILicenseServerClient
{
    Task<LicenseServerActivationResponse> ActivateAsync(LicenseServerActivationRequest request, CancellationToken cancellationToken = default);
    Task<LicenseServerStatusResponse> CheckStatusAsync(string activationId, string installationId, CancellationToken cancellationToken = default);
    Task DeactivateAsync(LicenseServerDeactivationRequest request, CancellationToken cancellationToken = default);
}
