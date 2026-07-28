using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreatorControlSuite.Core.Security;

public sealed record PairedAgentDevice(
    string Id,
    string Name,
    string Host,
    DateTimeOffset PairedAt,
    [property: JsonIgnore] string AgentKey,
    string CertificateFingerprint = "",
    string[]? AllowedCommands = null,
    string MacAddress = "",
    int AgentPort = 47631);

public sealed class PairedAgentRegistry
{
    private const string SecretKeyPrefix = "agent.device.";
    private const string SecretKeySuffix = ".api-key";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _registryPath;
    private readonly ISecretStore _secretStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PairedAgentRegistry(string registryPath, ISecretStore secretStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        _registryPath = registryPath;
        _secretStore = secretStore;
    }

    public async Task<IReadOnlyList<PairedAgentDevice>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<PersistedPairedAgentDevice> persisted =
                await ReadMetadataAsync(cancellationToken);
            var devices = new List<PairedAgentDevice>(persisted.Count);
            bool migratedLegacySecret = false;

            foreach (PersistedPairedAgentDevice item in persisted)
            {
                string secretKey = GetSecretKey(item.Id);
                string? apiKey = await _secretStore.LoadAsync(secretKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(apiKey) &&
                    !string.IsNullOrWhiteSpace(item.AgentKey))
                {
                    apiKey = item.AgentKey;
                    await _secretStore.SaveAsync(secretKey, apiKey, cancellationToken);
                    migratedLegacySecret = true;
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    continue;
                }

                devices.Add(ToRuntime(item, apiKey));
            }

            if (migratedLegacySecret)
            {
                await WriteMetadataAsync(
                    devices.Select(ToPersisted).ToList(),
                    cancellationToken);
            }

            return devices;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<PairedAgentDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (PairedAgentDevice device in devices)
            {
                Validate(device);
                await _secretStore.SaveAsync(
                    GetSecretKey(device.Id),
                    device.AgentKey,
                    cancellationToken);
            }

            await WriteMetadataAsync(
                devices.Select(ToPersisted).ToList(),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<PersistedPairedAgentDevice> persisted =
                await ReadMetadataAsync(cancellationToken);
            persisted.RemoveAll(device =>
                string.Equals(device.Id, deviceId, StringComparison.Ordinal));
            await _secretStore.DeleteAsync(GetSecretKey(deviceId), cancellationToken);
            await WriteMetadataAsync(persisted, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PersistedPairedAgentDevice>> ReadMetadataAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(_registryPath);
        return await JsonSerializer.DeserializeAsync<List<PersistedPairedAgentDevice>>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? [];
    }

    private async Task WriteMetadataAsync(
        List<PersistedPairedAgentDevice> devices,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _registryPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                devices,
                JsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _registryPath, overwrite: true);
    }

    private static string GetSecretKey(string deviceId) =>
        SecretKeyPrefix + deviceId + SecretKeySuffix;

    private static void Validate(PairedAgentDevice device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.AgentKey);
    }

    private static PairedAgentDevice ToRuntime(
        PersistedPairedAgentDevice device,
        string apiKey) =>
        new(
            device.Id,
            device.Name,
            device.Host,
            device.PairedAt,
            apiKey,
            device.CertificateFingerprint,
            device.AllowedCommands,
            device.MacAddress,
            device.AgentPort);

    private static PersistedPairedAgentDevice ToPersisted(PairedAgentDevice device) =>
        new(
            device.Id,
            device.Name,
            device.Host,
            device.PairedAt,
            null,
            device.CertificateFingerprint,
            device.AllowedCommands,
            device.MacAddress,
            device.AgentPort);

    private sealed record PersistedPairedAgentDevice(
        string Id,
        string Name,
        string Host,
        DateTimeOffset PairedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? AgentKey,
        string CertificateFingerprint = "",
        string[]? AllowedCommands = null,
        string MacAddress = "",
        int AgentPort = 47631);
}
