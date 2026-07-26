using System.Text.Json;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchTokenRepository
{
    private const string TokenKey = "twitch.tokenSet";
    private readonly ISecretStore _secretStore;

    public TwitchTokenRepository(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public async Task SaveAsync(
        TwitchTokenSet tokenSet,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(tokenSet);

        await _secretStore.SaveAsync(
            TokenKey,
            json,
            cancellationToken);
    }

    public async Task<TwitchTokenSet?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _secretStore.LoadAsync(
            TokenKey,
            cancellationToken);

        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<TwitchTokenSet>(json);
    }

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        return _secretStore.DeleteAsync(
            TokenKey,
            cancellationToken);
    }
}
