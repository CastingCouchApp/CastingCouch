using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyTokenRepository
{
    private readonly SecretJsonStore<SpotifyTokenSet> _store;

    public SpotifyTokenRepository(ISecretStore secretStore)
    {
        _store = new SecretJsonStore<SpotifyTokenSet>(
            secretStore,
            "spotify.tokenSet");
    }

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
