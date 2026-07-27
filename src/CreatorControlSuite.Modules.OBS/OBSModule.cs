using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.Modules.OBS;

public sealed class OBSModule(
    ISettingsStore settingsStore,
    ISecretStore secretStore,
    IObsWebSocketClient client) : IConnectableModule
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly ISecretStore _secretStore = secretStore;
    private readonly IObsWebSocketClient _client = client;

    public string Id => "obs";
    public string DisplayName => "OBS";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        string password =
            await _secretStore.LoadAsync(
                "obs.password",
                cancellationToken)
            ?? "";

        await _client.ConnectAsync(
            new ObsConnectionOptions(
                settings.Obs.Host,
                settings.Obs.Port,
                password,
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(8)),
            cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return _client.DisconnectAsync(cancellationToken);
    }

    public async Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Ready,
                "Nicht verbunden",
                DateTimeOffset.Now);
        }

        try
        {
            ObsSnapshot snapshot =
                await _client.GetSnapshotAsync(cancellationToken);

            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Connected,
                $"{snapshot.Server?.ObsVersion} · Szene: " +
                snapshot.CurrentProgramScene,
                DateTimeOffset.Now);
        }
        catch (Exception exception)
        {
            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Degraded,
                exception.Message,
                DateTimeOffset.Now);
        }
    }
}
