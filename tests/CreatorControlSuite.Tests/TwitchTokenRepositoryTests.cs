using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class TwitchTokenRepositoryTests
{
    [Fact]
    public async Task TokenSetCanBeStoredAndLoaded()
    {
        var store = new MemorySecretStore();
        var repository = new TwitchTokenRepository(store);

        var token = new TwitchTokenSet(
            "access",
            "refresh",
            3600,
            ["user:read:chat"],
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(token);

        TwitchTokenSet? loaded = await repository.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("access", loaded.AccessToken);
        Assert.Equal("refresh", loaded.RefreshToken);
    }

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
