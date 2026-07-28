using System.Text.Json;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class PairedAgentRegistryTests
{
    [Fact]
    public async Task LoadAsync_MigratesLegacyApiKey_AndSanitizesMetadata()
    {
        using var directory = new TemporaryDirectory();
        string registryPath = Path.Combine(directory.Path, "multi-pc-devices.json");
        await File.WriteAllTextAsync(
            registryPath,
            """
            [
              {
                "Id": "device-1",
                "Name": "Studio",
                "Host": "studio.local",
                "PairedAt": "2026-07-28T10:00:00+00:00",
                "AgentKey": "legacy-secret",
                "CertificateFingerprint": "AABB",
                "AllowedCommands": ["obs.control"],
                "MacAddress": "001122334455",
                "AgentPort": 47631
              }
            ]
            """);
        var secrets = new MemorySecretStore();
        var registry = new PairedAgentRegistry(registryPath, secrets);

        IReadOnlyList<PairedAgentDevice> devices = await registry.LoadAsync();

        PairedAgentDevice device = Assert.Single(devices);
        Assert.Equal("legacy-secret", device.AgentKey);
        Assert.Equal("legacy-secret", await secrets.LoadAsync("agent.device.device-1.api-key"));
        string sanitized = await File.ReadAllTextAsync(registryPath);
        Assert.DoesNotContain("legacy-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentKey", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAndDeleteAsync_KeepCredentialsOnlyInSecretStore()
    {
        using var directory = new TemporaryDirectory();
        string registryPath = Path.Combine(directory.Path, "multi-pc-devices.json");
        var secrets = new MemorySecretStore();
        var registry = new PairedAgentRegistry(registryPath, secrets);
        var device = new PairedAgentDevice(
            "device-2",
            "Regie",
            "regie.local",
            DateTimeOffset.UtcNow,
            "protected-key",
            "CCDD",
            ["obs.control"],
            "",
            47631);

        await registry.SaveAsync([device]);

        string metadata = await File.ReadAllTextAsync(registryPath);
        Assert.DoesNotContain("protected-key", metadata, StringComparison.Ordinal);
        Assert.Equal("protected-key", await secrets.LoadAsync("agent.device.device-2.api-key"));
        PairedAgentDevice loaded = Assert.Single(await registry.LoadAsync());
        Assert.Equal(device.Id, loaded.Id);
        Assert.Equal(device.Name, loaded.Name);
        Assert.Equal(device.Host, loaded.Host);
        Assert.Equal(device.AgentKey, loaded.AgentKey);
        Assert.Equal(device.CertificateFingerprint, loaded.CertificateFingerprint);
        Assert.Equal(device.AllowedCommands, loaded.AllowedCommands);

        await registry.DeleteAsync(device.Id);

        Assert.Null(await secrets.LoadAsync("agent.device.device-2.api-key"));
        Assert.Empty(await registry.LoadAsync());
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
