using System.Security.Cryptography;
using System.Text;

namespace CreatorControlSuite.Core.Updates;

public sealed class RsaUpdateSignatureVerifier : IUpdateSignatureVerifier
{
    private readonly string _publicKeyPath;

    public RsaUpdateSignatureVerifier(string publicKeyPath)
    {
        _publicKeyPath = publicKeyPath;
    }

    public bool VerifyManifest(SignedUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(manifest.Signature) ||
            !File.Exists(_publicKeyPath))
        {
            return false;
        }

        try
        {
            var payload = Encoding.UTF8.GetBytes(
                UpdateManifestCanonical.GetPayload(manifest));
            var signature = Convert.FromBase64String(manifest.Signature);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(_publicKeyPath));

            return rsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> VerifyPackageAsync(
        string packagePath,
        SignedUpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!File.Exists(packagePath))
        {
            return false;
        }

        var info = new FileInfo(packagePath);
        if (info.Length != manifest.PackageSizeBytes)
        {
            return false;
        }

        await using var stream = File.OpenRead(packagePath);
        var hash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));

        return hash.Equals(
            manifest.PackageSha256,
            StringComparison.OrdinalIgnoreCase);
    }
}
