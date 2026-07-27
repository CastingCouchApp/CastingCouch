using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Licensing;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class LicenseFeatureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public async Task DevelopmentMode_ReturnsWildcard()
    {
        var service = new LocalLicenseService(
            new MemorySecretStore(),
            publicKeyPath: "unused.pem",
            developmentMode: true);

        var status = await service.GetStatusAsync();

        Assert.Equal(LicenseState.Development, status.State);
        Assert.Contains("*", status.EnabledFeatures);
        Assert.True(status.IsUsable);
    }

    [Fact]
    public async Task ActivateValidLicense_Active()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Creator",
                features: [FeatureCatalog.Twitch],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        var service = CreateService(keys);
        var path = WriteLicenseFile(document);

        try
        {
            var status = await service.ActivateAsync(path);

            Assert.Equal(LicenseState.Active, status.State);
            Assert.True(status.IsUsable);
            Assert.Contains(FeatureCatalog.Twitch, status.EnabledFeatures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidSignature_Invalid()
    {
        using var keys = RsaKeyPair.Create();
        var other = RsaKeyPair.Create();
        var document = Sign(
            other,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Creator",
                features: [FeatureCatalog.Twitch],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));
        other.Dispose();

        var service = CreateService(keys);
        var path = WriteLicenseFile(document);

        try
        {
            var status = await service.ActivateAsync(path);

            Assert.Equal(LicenseState.Invalid, status.State);
            Assert.False(status.IsUsable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExpiredLicense_Expired()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Creator",
                features: [FeatureCatalog.Twitch],
                expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var service = CreateService(keys);
        var path = WriteLicenseFile(document);

        try
        {
            var status = await service.ActivateAsync(path);

            Assert.Equal(LicenseState.Expired, status.State);
            Assert.False(status.IsUsable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WrongProduct_Invalid()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "other-product",
                edition: "Creator",
                features: [FeatureCatalog.Twitch],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        var service = CreateService(keys);
        var path = WriteLicenseFile(document);

        try
        {
            var status = await service.ActivateAsync(path);

            Assert.Equal(LicenseState.Invalid, status.State);
            Assert.Contains("anderen Produkt", status.Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FeatureGate_EditionFallback_CreatorHasTwitch()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Creator",
                features: [],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        var service = CreateService(keys);
        await ActivateStored(service, document);
        var gate = new FeatureGate(service);

        Assert.True(await gate.IsEnabledAsync(FeatureCatalog.Twitch));
    }

    [Fact]
    public async Task FeatureGate_Require_ThrowsWhenMissing()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Core",
                features: [],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        var service = CreateService(keys);
        await ActivateStored(service, document);
        var gate = new FeatureGate(service);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.RequireAsync(FeatureCatalog.Twitch));
    }

    [Fact]
    public async Task FeatureGate_WildcardEnablesAll()
    {
        var service = new LocalLicenseService(
            new MemorySecretStore(),
            publicKeyPath: "unused.pem",
            developmentMode: true);
        var gate = new FeatureGate(service);

        Assert.True(await gate.IsEnabledAsync(FeatureCatalog.CommercialUse));
        Assert.True(await gate.IsEnabledAsync(FeatureCatalog.Spotify));
    }

    [Fact]
    public async Task HasFeature_OnlyChecksEnabledFeaturesNotEdition()
    {
        using var keys = RsaKeyPair.Create();
        var document = Sign(
            keys,
            CreateDocument(
                productId: "creator-control-suite",
                edition: "Creator",
                features: [],
                expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        var service = CreateService(keys);
        await ActivateStored(service, document);
        var gate = new FeatureGate(service);

        Assert.False(await service.HasFeatureAsync(FeatureCatalog.Twitch));
        Assert.True(await gate.IsEnabledAsync(FeatureCatalog.Twitch));
    }

    private static LocalLicenseService CreateService(RsaKeyPair keys)
        => new(new MemorySecretStore(), keys.PublicKeyPath, developmentMode: false);

    private static async Task ActivateStored(
        LocalLicenseService service,
        LicenseDocument document)
    {
        var path = WriteLicenseFile(document);
        try
        {
            var status = await service.ActivateAsync(path);
            Assert.True(status.IsUsable, status.Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LicenseDocument CreateDocument(
        string productId,
        string edition,
        IReadOnlyList<string> features,
        DateTimeOffset? expiresAt)
        => new(
            LicenseId: Guid.NewGuid().ToString("N"),
            ProductId: productId,
            Edition: edition,
            CustomerName: "Test",
            CustomerEmail: "test@example.com",
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            Features: features,
            Signature: "");

    private static LicenseDocument Sign(RsaKeyPair keys, LicenseDocument document)
    {
        var payload = JsonSerializer.Serialize(
            document with { Signature = "" },
            JsonOptions);
        var signature = Convert.ToBase64String(
            keys.Rsa.SignData(
                Encoding.UTF8.GetBytes(payload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));
        return document with { Signature = signature };
    }

    private static string WriteLicenseFile(LicenseDocument document)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ccs-license-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
        return path;
    }

    private sealed class RsaKeyPair : IDisposable
    {
        private RsaKeyPair(RSA rsa, string directory, string publicKeyPath)
        {
            Rsa = rsa;
            Directory = directory;
            PublicKeyPath = publicKeyPath;
        }

        public RSA Rsa { get; }
        public string Directory { get; }
        public string PublicKeyPath { get; }

        public static RsaKeyPair Create()
        {
            var rsa = RSA.Create(2048);
            var directory = Path.Combine(
                Path.GetTempPath(),
                "CreatorControlSuite.Tests",
                "licenses",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var publicKeyPath = Path.Combine(directory, "license-public.pem");
            File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
            return new RsaKeyPair(rsa, directory, publicKeyPath);
        }

        public void Dispose()
        {
            Rsa.Dispose();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new();

        public Task SaveAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
