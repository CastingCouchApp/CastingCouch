using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchTokenRepository
{
    private readonly SecretJsonStore<TwitchTokenSet> _store;

    public TwitchTokenRepository(ISecretStore secretStore)
    {
        _store = new SecretJsonStore<TwitchTokenSet>(
            secretStore,
            "twitch.tokenSet");
    }

    public Task SaveAsync(
        TwitchTokenSet tokenSet,
        CancellationToken cancellationToken = default)
        => _store.SaveAsync(tokenSet, cancellationToken);

    public Task<TwitchTokenSet?> LoadAsync(
        CancellationToken cancellationToken = default)
        => _store.LoadAsync(cancellationToken);

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
        => _store.DeleteAsync(cancellationToken);
}
