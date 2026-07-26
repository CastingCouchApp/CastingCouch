using System.Security.Cryptography; using System.Text; using System.Text.Json;
namespace CreatorControlSuite.Core.Updates;
public sealed class RsaUpdateSignatureVerifier : IUpdateSignatureVerifier
{
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true}; readonly string _publicKeyPath;
    public RsaUpdateSignatureVerifier(string publicKeyPath)=>_publicKeyPath=publicKeyPath;
    public bool VerifyManifest(SignedUpdateManifest m)
    {
        if(!File.Exists(_publicKeyPath)) return false;
        try { var payload=JsonSerializer.Serialize(m with { Signature="" },Options); using var rsa=RSA.Create(); rsa.ImportFromPem(File.ReadAllText(_publicKeyPath)); return rsa.VerifyData(Encoding.UTF8.GetBytes(payload),Convert.FromBase64String(m.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1); } catch { return false; }
    }
    public async Task<bool> VerifyPackageAsync(string path,SignedUpdateManifest m,CancellationToken ct=default)
    { if(!File.Exists(path)) return false; await using var s=File.OpenRead(path); var hash=Convert.ToHexString(await SHA256.HashDataAsync(s,ct)); return hash.Equals(m.PackageSha256,StringComparison.OrdinalIgnoreCase)&&new FileInfo(path).Length==m.PackageSizeBytes; }
}
