using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyTokenRepository(ISecretStore secretStore)
{
    private readonly SecretJsonStore<SpotifyTokenSet> _store = new(
            secretStore,
            "spotify.tokenSet");

    public Task SaveAsync(
        SpotifyTokenSet tokenSet,
        CancellationToken cancellationToken = default)
        => _store.SaveAsync(tokenSet, cancellationToken);

    public Task<SpotifyTokenSet?> LoadAsync(
        CancellationToken cancellationToken = default)
        => _store.LoadAsync(cancellationToken);

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
        => _store.DeleteAsync(cancellationToken);
}
