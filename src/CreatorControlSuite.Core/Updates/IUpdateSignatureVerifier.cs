namespace CreatorControlSuite.Core.Updates;

public interface IUpdateSignatureVerifier
{
    bool VerifyManifest(SignedUpdateManifest manifest);
    Task<bool> VerifyPackageAsync(string packagePath, SignedUpdateManifest manifest, CancellationToken cancellationToken = default);
}
