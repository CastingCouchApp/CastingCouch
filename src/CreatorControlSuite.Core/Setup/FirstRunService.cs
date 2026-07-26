using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Setup;

public sealed class FirstRunService : IFirstRunService
{
    private const int CurrentWizardVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _statePath;
    private readonly ISettingsStore _settingsStore;

    public FirstRunService(
        string statePath,
        ISettingsStore settingsStore)
    {
        _statePath = statePath;
        _settingsStore = settingsStore;
    }

    public async Task<FirstRunState> LoadStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return new FirstRunState();
        }

        await using var stream = File.OpenRead(_statePath);

        return await JsonSerializer.DeserializeAsync<FirstRunState>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? new FirstRunState();
    }

    public async Task SaveStateAsync(
        FirstRunState state,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);

        var temporaryPath = _statePath + ".tmp";

        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(state, JsonOptions),
            cancellationToken);

        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    public async Task<bool> IsRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);

        return !state.Completed ||
               state.CompletedVersion < CurrentWizardVersion;
    }

    public async Task<FirstRunSummary> BuildSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        return new FirstRunSummary(
            settings.Branding.DisplayName,
            settings.Twitch.ChannelName,
            settings.Obs.Host,
            settings.Obs.Port,
            settings.Obs.StartScene,
            settings.Obs.LiveScene,
            settings.Obs.PauseScene,
            settings.Obs.EndScene,
            settings.Overlay.RootPath);
    }
}
