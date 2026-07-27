using System.Security.Cryptography;
using System.Text;

namespace CreatorControlSuite.Core.Updates;

public sealed class RsaUpdateSignatureVerifier(string publicKeyPath) : IUpdateSignatureVerifier
{
    private readonly string _publicKeyPath = publicKeyPath;

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
            byte[] payload = Encoding.UTF8.GetBytes(
                UpdateManifestCanonical.GetPayload(manifest));
            byte[] signature = Convert.FromBase64String(manifest.Signature);

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

        await using FileStream stream = File.OpenRead(packagePath);
        string hash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));

        return hash.Equals(
            manifest.PackageSha256,
            StringComparison.OrdinalIgnoreCase);
    }
}
