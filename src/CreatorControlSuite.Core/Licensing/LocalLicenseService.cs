using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Security;
namespace CreatorControlSuite.Core.Licensing;

public sealed class LocalLicenseService(ISecretStore secrets, string publicKeyPath, bool developmentMode) : ILicenseService
{
    private const string ProductId = "creator-control-suite", StoredKey = "license.document";
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly ISecretStore _secrets = secrets; private readonly string _publicKeyPath = publicKeyPath; private readonly bool _dev = developmentMode;

    public async Task<LicenseStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (_dev)
        {
            return new(LicenseState.Development, "Entwicklungsmodus aktiv.", null, ["*"]);
        }

        string? json = await _secrets.LoadAsync(StoredKey, ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(LicenseState.Unknown, "Keine Lizenz aktiviert.", null, []);
        }

        try { return Validate(JsonSerializer.Deserialize<LicenseDocument>(json, Options) ?? throw new InvalidOperationException("Lizenzdokument ist leer.")); }
        catch (Exception ex) { return new(LicenseState.Invalid, ex.Message, null, []); }
    }
    public async Task<LicenseStatus> ActivateAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Lizenzdatei wurde nicht gefunden.", path);
        }

        LicenseDocument doc = JsonSerializer.Deserialize<LicenseDocument>(await File.ReadAllTextAsync(path, ct), Options) ?? throw new InvalidOperationException("Lizenzdatei konnte nicht gelesen werden.");
        LicenseStatus status = Validate(doc); if (!status.IsUsable)
        {
            return status;
        }

        await _secrets.SaveAsync(StoredKey, JsonSerializer.Serialize(doc, Options), ct); return status;
    }
    public Task DeactivateAsync(CancellationToken ct = default) => _secrets.DeleteAsync(StoredKey, ct);
    public async Task<bool> HasFeatureAsync(string feature, CancellationToken ct = default)
    { LicenseStatus s = await GetStatusAsync(ct); return s.IsUsable && (s.EnabledFeatures.Contains("*", StringComparer.OrdinalIgnoreCase) || s.EnabledFeatures.Contains(feature, StringComparer.OrdinalIgnoreCase)); }
    private LicenseStatus Validate(LicenseDocument d)
    {
        if (!string.Equals(d.ProductId, ProductId, StringComparison.Ordinal))
        {
            return new(LicenseState.Invalid, "Lizenz gehört zu einem anderen Produkt.", d, []);
        }

        if (!Verify(d))
        {
            return new(LicenseState.Invalid, "Lizenzsignatur ist ungültig.", d, []);
        }

        if (d.ExpiresAt is not null && d.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return new(LicenseState.Expired, "Lizenz ist abgelaufen.", d, d.Features);
        }

        return new(LicenseState.Active, "Lizenz aktiv.", d, d.Features);
    }
    private bool Verify(LicenseDocument d)
    {
        if (!File.Exists(_publicKeyPath))
        {
            throw new FileNotFoundException("Lizenz-Public-Key fehlt.", _publicKeyPath);
        }

        string payload = JsonSerializer.Serialize(d with { Signature = "" }, Options); using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(_publicKeyPath));
        return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(d.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
