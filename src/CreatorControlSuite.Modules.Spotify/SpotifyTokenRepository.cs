using System.Text.Json;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyTokenRepository
{
    private const string TokenKey = "spotify.tokenSet";
    private readonly ISecretStore _secretStore;

    public SpotifyTokenRepository(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public async Task SaveAsync(
        SpotifyTokenSet tokenSet,
        CancellationToken cancellationToken = default)
    {
        await _secretStore.SaveAsync(
            TokenKey,
            JsonSerializer.Serialize(tokenSet),
            cancellationToken);
    }

    public async Task<SpotifyTokenSet?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _secretStore.LoadAsync(
            TokenKey,
            cancellationToken);

        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<SpotifyTokenSet>(json);
    }

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        return _secretStore.DeleteAsync(
            TokenKey,
            cancellationToken);
    }
}
