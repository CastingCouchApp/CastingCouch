using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchTokenRepository(ISecretStore secretStore)
{
    private readonly SecretJsonStore<TwitchTokenSet> _store = new(
            secretStore,
            "twitch.tokenSet");

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
