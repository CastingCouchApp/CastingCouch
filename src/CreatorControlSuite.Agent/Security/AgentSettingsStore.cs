using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Agent.Security;

public sealed record AgentSettings(
    string ObsPath = "",
    string StreamerBotPath = "",
    string ObsWebSocketHost = "127.0.0.1",
    int ObsWebSocketPort = 4455,
    [property: JsonIgnore] string ObsWebSocketPassword = "",
    string OverlayDirectory = "",
    string UpdateStagingDirectory = "",
    string SuiteInstallDirectory = "",
    string SuiteExecutablePath = "");

public sealed class AgentSettingsStore
{
    public const string ObsPasswordSecretKey = "agent.obs-websocket.password";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly ISecretStore _secretStore;

    public AgentSettingsStore(string settingsPath, ISecretStore secretStore)
    {
        _settingsPath = settingsPath;
        _secretStore = secretStore;
    }

    public async Task<AgentSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        PersistedAgentSettings persisted = File.Exists(_settingsPath)
            ? await ReadAsync(cancellationToken)
            : new PersistedAgentSettings();
        string? password = await _secretStore.LoadAsync(
            ObsPasswordSecretKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(password) &&
            !string.IsNullOrWhiteSpace(persisted.ObsWebSocketPassword))
        {
            password = persisted.ObsWebSocketPassword;
            await _secretStore.SaveAsync(
                ObsPasswordSecretKey,
                password,
                cancellationToken);
            await WritePublicAsync(ToRuntime(persisted, password), cancellationToken);
        }

        return ToRuntime(persisted, password ?? "");
    }

    public async Task SaveAsync(
        AgentSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(settings.ObsWebSocketPassword))
        {
            await _secretStore.DeleteAsync(ObsPasswordSecretKey, cancellationToken);
        }
        else
        {
            await _secretStore.SaveAsync(
                ObsPasswordSecretKey,
                settings.ObsWebSocketPassword,
                cancellationToken);
        }

        await WritePublicAsync(settings, cancellationToken);
    }

    private async Task<PersistedAgentSettings> ReadAsync(
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<PersistedAgentSettings>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? new PersistedAgentSettings();
    }

    private async Task WritePublicAsync(
        AgentSettings settings,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var persisted = new PersistedAgentSettings(
            settings.ObsPath,
            settings.StreamerBotPath,
            settings.ObsWebSocketHost,
            settings.ObsWebSocketPort,
            null,
            settings.OverlayDirectory,
            settings.UpdateStagingDirectory,
            settings.SuiteInstallDirectory,
            settings.SuiteExecutablePath);
        string temporaryPath = _settingsPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                persisted,
                JsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    private static AgentSettings ToRuntime(
        PersistedAgentSettings settings,
        string password) =>
        new(
            settings.ObsPath,
            settings.StreamerBotPath,
            settings.ObsWebSocketHost,
            settings.ObsWebSocketPort,
            password,
            settings.OverlayDirectory,
            settings.UpdateStagingDirectory,
            settings.SuiteInstallDirectory,
            settings.SuiteExecutablePath);

    private sealed record PersistedAgentSettings(
        string ObsPath = "",
        string StreamerBotPath = "",
        string ObsWebSocketHost = "127.0.0.1",
        int ObsWebSocketPort = 4455,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ObsWebSocketPassword = null,
        string OverlayDirectory = "",
        string UpdateStagingDirectory = "",
        string SuiteInstallDirectory = "",
        string SuiteExecutablePath = "");
}
