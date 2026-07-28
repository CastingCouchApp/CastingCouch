using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class SecretProtectedSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_MigratesLegacyPassword_AndSanitizesBackingStore()
    {
        var legacy = new AppSettings();
        legacy.StreamerBot.Password = "legacy-password";
        var inner = new MemorySettingsStore(legacy);
        var secrets = new MemorySecretStore();
        var store = new SecretProtectedSettingsStore(inner, secrets);

        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("legacy-password", loaded.StreamerBot.Password);
        Assert.Equal(
            "legacy-password",
            await secrets.LoadAsync(SecretProtectedSettingsStore.StreamerBotPasswordKey));
        Assert.Equal("", inner.Stored.StreamerBot.Password);
    }

    [Fact]
    public async Task SaveAsync_PersistsPasswordOnlyInSecretStore()
    {
        var settings = new AppSettings();
        settings.StreamerBot.Password = "new-password";
        var inner = new MemorySettingsStore(new AppSettings());
        var secrets = new MemorySecretStore();
        var store = new SecretProtectedSettingsStore(inner, secrets);

        await store.SaveAsync(settings);

        Assert.Equal("", inner.Stored.StreamerBot.Password);
        Assert.Equal("new-password", settings.StreamerBot.Password);
        Assert.Equal(
            "new-password",
            await secrets.LoadAsync(SecretProtectedSettingsStore.StreamerBotPasswordKey));
    }

    private sealed class MemorySettingsStore(AppSettings initial) : ISettingsStore
    {
        public AppSettings Stored { get; private set; } = Clone(initial);

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Clone(Stored));

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            Stored = Clone(settings);
            return Task.CompletedTask;
        }

        private static AppSettings Clone(AppSettings settings)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(settings);
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        }
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
}
