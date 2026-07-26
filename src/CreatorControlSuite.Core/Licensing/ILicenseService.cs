namespace CreatorControlSuite.Core.Licensing;
public interface ILicenseService
{
    Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken=default);
    Task<LicenseStatus> ActivateAsync(string licenseFilePath,CancellationToken cancellationToken=default);
    Task DeactivateAsync(CancellationToken cancellationToken=default);
    Task<bool> HasFeatureAsync(string feature,CancellationToken cancellationToken=default);
}
