using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class SecretJsonStoreTests
{
    [Fact]
    public async Task SaveLoadDelete_RoundTripsValue()
    {
        var secrets = new MemorySecretStore();
        var store = new SecretJsonStore<SamplePayload>(secrets, "sample.key");

        await store.SaveAsync(new SamplePayload("alpha", 7));

        SamplePayload? loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("alpha", loaded.Name);
        Assert.Equal(7, loaded.Count);

        await store.DeleteAsync();

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefault_WhenMissing()
    {
        var store = new SecretJsonStore<SamplePayload>(
            new MemorySecretStore(),
            "missing.key");

        Assert.Null(await store.LoadAsync());
    }

    private sealed record SamplePayload(string Name, int Count);

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

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
            _values.TryGetValue(key, out string? value);
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
