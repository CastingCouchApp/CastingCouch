using System.Security.Cryptography;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Agent.Security;

public sealed record AgentCredential(
    string DeviceId,
    string DisplayName,
    string ApiKey,
    DateTimeOffset PairedAt,
    DateTimeOffset LastRotatedAt);

public sealed class AgentCredentialStore
{
    private const string CredentialsSecretKey = "agent.credentials.v1";
    private readonly SecretJsonStore<List<AgentCredential>> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentCredentialStore(ISecretStore secretStore)
    {
        _store = new SecretJsonStore<List<AgentCredential>>(
            secretStore,
            CredentialsSecretKey);
    }

    public async Task<IReadOnlyList<AgentCredential>> LoadAndMigrateAsync(
        string legacyKeyPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<AgentCredential> credentials = await _store.LoadAsync(cancellationToken) ?? [];
            if (credentials.Count > 0 || !File.Exists(legacyKeyPath))
            {
                return credentials;
            }

            string legacyKey = (await File.ReadAllTextAsync(
                legacyKeyPath,
                cancellationToken)).Trim();
            if (string.IsNullOrWhiteSpace(legacyKey))
            {
                File.Delete(legacyKeyPath);
                return credentials;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            credentials.Add(new AgentCredential(
                "legacy",
                "Legacy Client",
                legacyKey,
                now,
                now));
            await _store.SaveAsync(credentials, cancellationToken);
            File.Delete(legacyKeyPath);
            return credentials;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentCredential> AddAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<AgentCredential> credentials = await _store.LoadAsync(cancellationToken) ?? [];
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var credential = new AgentCredential(
                Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(displayName) ? "CastingCouch" : displayName.Trim(),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                now,
                now);
            credentials.Add(credential);
            await _store.SaveAsync(credentials, cancellationToken);
            return credential;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentCredential?> RotateAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<AgentCredential> credentials = await _store.LoadAsync(cancellationToken) ?? [];
            int index = credentials.FindIndex(item =>
                string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
            if (index < 0)
            {
                return null;
            }

            AgentCredential rotated = credentials[index] with
            {
                ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                LastRotatedAt = DateTimeOffset.UtcNow
            };
            credentials[index] = rotated;
            await _store.SaveAsync(credentials, cancellationToken);
            return rotated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<AgentCredential> credentials = await _store.LoadAsync(cancellationToken) ?? [];
            int removed = credentials.RemoveAll(item =>
                string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            await _store.SaveAsync(credentials, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static AgentCredential? Authenticate(
        IReadOnlyList<AgentCredential> credentials,
        string? suppliedKey)
    {
        byte[] supplied = System.Text.Encoding.UTF8.GetBytes(suppliedKey ?? "");
        foreach (AgentCredential credential in credentials)
        {
            byte[] expected = System.Text.Encoding.UTF8.GetBytes(credential.ApiKey);
            if (expected.Length == supplied.Length &&
                CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                return credential;
            }
        }

        return null;
    }
}
