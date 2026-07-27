using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyTokenRepositoryTests
{
    [Fact]
    public async Task TokenSetCanBeStoredAndLoaded()
    {
        var store = new MemorySecretStore();
        var repository =
            new SpotifyTokenRepository(store);

        var token = new SpotifyTokenSet(
            "access",
            "refresh",
            3600,
            "Bearer",
            ["user-read-playback-state"],
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(token);

        SpotifyTokenSet? loaded = await repository.LoadAsync();

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
