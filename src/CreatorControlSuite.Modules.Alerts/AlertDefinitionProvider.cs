using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Modules.Alerts;

public sealed class AlertDefinitionProvider(ISettingsStore settingsStore)
{
    private readonly ISettingsStore _settingsStore = settingsStore;

    public async Task<AlertDefinition> GetAsync(
        string type,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(
            cancellationToken);

        if (!settings.Alerts.Definitions.TryGetValue(
                type,
                out AlertDefinitionSettings? definition))
        {
            throw new InvalidOperationException(
                "Unbekannter Alert-Typ: " + type);
        }

        return new AlertDefinition(
            definition.Type,
            definition.Enabled,
            definition.TextTemplate,
            definition.MediaPath,
            definition.SoundPath,
            TimeSpan.FromSeconds(
                Math.Max(1, definition.DurationSeconds)),
            definition.Priority,
            definition.FontFace,
            definition.FontSize,
            definition.FontColor,
            definition.Animation,
            definition.X,
            definition.Y,
            definition.Width,
            definition.Height,
            definition.VolumePercent);
    }

    public async Task<IReadOnlyList<AlertDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(
            cancellationToken);

        return [.. settings.Alerts.Definitions.Values
            .Select(definition => new AlertDefinition(
                definition.Type,
                definition.Enabled,
                definition.TextTemplate,
                definition.MediaPath,
                definition.SoundPath,
                TimeSpan.FromSeconds(
                    Math.Max(1, definition.DurationSeconds)),
                definition.Priority,
                definition.FontFace,
                definition.FontSize,
                definition.FontColor,
                definition.Animation,
                definition.X,
                definition.Y,
                definition.Width,
                definition.Height,
                definition.VolumePercent))
            .OrderBy(definition => definition.Priority)];
    }
}
