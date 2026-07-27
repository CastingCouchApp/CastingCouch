using System.Text.Json;

namespace CreatorControlSuite.Core.Security;

/// <summary>
/// Typed JSON persistence over <see cref="ISecretStore"/>.
/// Shared by OAuth token repositories (Twitch, Spotify, …).
/// </summary>
public sealed class SecretJsonStore<T>
{
    private readonly ISecretStore _secretStore;
    private readonly string _key;
    private readonly JsonSerializerOptions? _options;

    public SecretJsonStore(
        ISecretStore secretStore,
        string key,
        JsonSerializerOptions? options = null)
    {
        _secretStore = secretStore;
        _key = key;
        _options = options;
    }

    public async Task SaveAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, _options);
        await _secretStore.SaveAsync(_key, json, cancellationToken);
    }

    public async Task<T?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _secretStore.LoadAsync(_key, cancellationToken);
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, _options);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
        => _secretStore.DeleteAsync(_key, cancellationToken);
}
