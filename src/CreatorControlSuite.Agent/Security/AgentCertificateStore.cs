using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Agent.Security;

public sealed class AgentCertificateStore(
    string certificatePath,
    ISecretStore secretStore)
{
    public const string CertificatePasswordSecretKey = "agent.tls.pfx-password";

    public async Task<X509Certificate2> LoadOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        string? password = await secretStore.LoadAsync(
            CertificatePasswordSecretKey,
            cancellationToken);
        if (File.Exists(certificatePath) && !string.IsNullOrWhiteSpace(password))
        {
            return Load(password);
        }

        password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        X509Certificate2 certificate = File.Exists(certificatePath)
            ? X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                null,
                X509KeyStorageFlags.Exportable)
            : CreateCertificate();

        await secretStore.SaveAsync(
            CertificatePasswordSecretKey,
            password,
            cancellationToken);
        await File.WriteAllBytesAsync(
            certificatePath,
            certificate.Export(X509ContentType.Pfx, password),
            cancellationToken);
        certificate.Dispose();
        return Load(password);
    }

    private X509Certificate2 Load(string password) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.EphemeralKeySet
                : X509KeyStorageFlags.DefaultKeySet);

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=CreatorControlSuite.Agent",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
    }
}
