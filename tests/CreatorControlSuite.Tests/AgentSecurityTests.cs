using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class AgentSecurityTests
{
    [Fact]
    public async Task CredentialStore_MigratesAndDeletesLegacyPlaintextKey()
    {
        using var directory = new TemporaryDirectory();
        string legacyPath = Path.Combine(directory.Path, "agent-key.txt");
        await File.WriteAllTextAsync(legacyPath, "legacy-agent-key");
        var secrets = new MemorySecretStore();
        var store = new AgentCredentialStore(secrets);

        IReadOnlyList<AgentCredential> credentials =
            await store.LoadAndMigrateAsync(legacyPath);

        AgentCredential credential = Assert.Single(credentials);
        Assert.Equal("legacy-agent-key", credential.ApiKey);
        Assert.False(File.Exists(legacyPath));
        Assert.DoesNotContain("legacy-agent-key", Directory.GetFiles(directory.Path)
            .Select(File.ReadAllText));
    }

    [Fact]
    public async Task SettingsStore_MigratesObsPassword_AndRewritesPublicSettings()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "agent-settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "ObsWebSocketHost": "127.0.0.1",
              "ObsWebSocketPort": 4455,
              "ObsWebSocketPassword": "plain-password",
              "ObsPath": "obs64.exe"
            }
            """);
        var secrets = new MemorySecretStore();
        var store = new AgentSettingsStore(settingsPath, secrets);

        AgentSettings settings = await store.LoadAsync();

        Assert.Equal("plain-password", settings.ObsWebSocketPassword);
        Assert.Equal("plain-password", await secrets.LoadAsync(AgentSettingsStore.ObsPasswordSecretKey));
        string sanitized = await File.ReadAllTextAsync(settingsPath);
        Assert.DoesNotContain("plain-password", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("ObsWebSocketPassword", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void PairingSession_ExpiresAndLocksAfterMaximumFailedAttempts()
    {
        DateTimeOffset now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
        var session = new PairingSession(
            "123456",
            now,
            TimeSpan.FromMinutes(5),
            maximumFailedAttempts: 3);

        Assert.Equal(PairingAttemptResult.InvalidCode, session.TryConsume("000000", now));
        Assert.Equal(PairingAttemptResult.InvalidCode, session.TryConsume("000000", now));
        Assert.Equal(PairingAttemptResult.Locked, session.TryConsume("000000", now));
        Assert.Equal(PairingAttemptResult.Locked, session.TryConsume("123456", now));

        var expired = new PairingSession("123456", now, TimeSpan.FromMinutes(5), 3);
        Assert.Equal(
            PairingAttemptResult.Expired,
            expired.TryConsume("123456", now.AddMinutes(6)));
    }

    [Fact]
    public void PairingSession_AllowsOneLegitimateUse()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var session = new PairingSession("123456", now, TimeSpan.FromMinutes(5), 5);

        Assert.Equal(PairingAttemptResult.Accepted, session.TryConsume("123456", now));
        Assert.Equal(PairingAttemptResult.Consumed, session.TryConsume("123456", now));
    }

    [Fact]
    public async Task CertificateStore_MigratesUnprotectedPfx_ToDpapiBackedPassword()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "agent-certificate.pfx");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=legacy-agent",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 legacy = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        await File.WriteAllBytesAsync(path, legacy.Export(X509ContentType.Pfx));
        var secrets = new MemorySecretStore();
        var store = new AgentCertificateStore(path, secrets);

        using X509Certificate2 migrated = await store.LoadOrCreateAsync();
        string? password = await secrets.LoadAsync(
            AgentCertificateStore.CertificatePasswordSecretKey);

        Assert.True(migrated.HasPrivateKey);
        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.ThrowsAny<CryptographicException>(
            () => X509CertificateLoader.LoadPkcs12FromFile(path, null));
        using X509Certificate2 reloaded =
            X509CertificateLoader.LoadPkcs12FromFile(path, password);
        Assert.Equal(migrated.Thumbprint, reloaded.Thumbprint);
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out string? value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CreatorControlSuite.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
