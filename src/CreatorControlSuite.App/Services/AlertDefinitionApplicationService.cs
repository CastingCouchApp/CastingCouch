using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public interface IAlertDefinitionApplicationService
{
    Task<AlertDefinitionSettings> CreateAsync(
        AppSettings settings,
        string baseType,
        CancellationToken cancellationToken = default);

    Task<AlertDefinitionSettings> DuplicateAsync(
        AppSettings settings,
        string sourceType,
        CancellationToken cancellationToken = default);

    Task<AlertDefinitionSettings> ToggleAsync(
        AppSettings settings,
        string type,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        AppSettings settings,
        string type,
        CancellationToken cancellationToken = default);
}

public sealed class AlertDefinitionApplicationService(
    ISettingsStore settingsStore) : IAlertDefinitionApplicationService
{
    public async Task<AlertDefinitionSettings> CreateAsync(
        AppSettings settings,
        string baseType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureDefinitions(settings);
        string type = CreateUniqueType(
            settings.Alerts.Definitions,
            baseType);
        var definition = new AlertDefinitionSettings
        {
            Type = type,
            Enabled = true,
            TextTemplate = "{user} hat einen Alert ausgelöst!"
        };
        settings.Alerts.Definitions[type] = definition;

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Alerts.Definitions.Remove(type);
            throw;
        }

        return definition;
    }

    public async Task<AlertDefinitionSettings> DuplicateAsync(
        AppSettings settings,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AlertDefinitionSettings source =
            FindDefinition(settings, sourceType);
        string type = CreateUniqueType(
            settings.Alerts.Definitions,
            source.Type + " Kopie");
        AlertDefinitionSettings duplicate = Clone(source, type);
        settings.Alerts.Definitions[type] = duplicate;

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Alerts.Definitions.Remove(type);
            throw;
        }

        return duplicate;
    }

    public async Task<AlertDefinitionSettings> ToggleAsync(
        AppSettings settings,
        string type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AlertDefinitionSettings definition =
            FindDefinition(settings, type);
        bool previous = definition.Enabled;
        definition.Enabled = !previous;

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            definition.Enabled = previous;
            throw;
        }

        return definition;
    }

    public async Task DeleteAsync(
        AppSettings settings,
        string type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureDefinitions(settings);
        if (settings.Alerts.Definitions.Count <= 1)
        {
            throw new InvalidOperationException(
                "Mindestens ein Alert muss erhalten bleiben.");
        }

        AlertDefinitionSettings definition =
            FindDefinition(settings, type);
        settings.Alerts.Definitions.Remove(type);

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Alerts.Definitions[type] = definition;
            throw;
        }
    }

    private static AlertDefinitionSettings FindDefinition(
        AppSettings settings,
        string type)
    {
        EnsureDefinitions(settings);
        return settings.Alerts.Definitions.TryGetValue(
            type,
            out AlertDefinitionSettings? definition)
            ? definition
            : throw new InvalidOperationException(
                $"Alert '{type}' wurde nicht gefunden.");
    }

    private static void EnsureDefinitions(AppSettings settings)
    {
        settings.Alerts ??= new AlertSettings();
        settings.Alerts.Definitions ??=
            new Dictionary<string, AlertDefinitionSettings>(
                StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateUniqueType(
        IReadOnlyDictionary<string, AlertDefinitionSettings> definitions,
        string baseType)
    {
        string cleaned = string.IsNullOrWhiteSpace(baseType)
            ? "Eigener Alert"
            : baseType.Trim();
        if (!definitions.ContainsKey(cleaned))
        {
            return cleaned;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = $"{cleaned} {suffix}";
            if (!definitions.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return cleaned + " " + Guid.NewGuid().ToString("N")[..6];
    }

    private static AlertDefinitionSettings Clone(
        AlertDefinitionSettings source,
        string type) =>
        new()
        {
            Type = type,
            Enabled = source.Enabled,
            TextTemplate = source.TextTemplate,
            MediaPath = source.MediaPath,
            SoundPath = source.SoundPath,
            DurationSeconds = source.DurationSeconds,
            Priority = source.Priority,
            FontFace = source.FontFace,
            FontSize = source.FontSize,
            FontColor = source.FontColor,
            Animation = source.Animation,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
            VolumePercent = source.VolumePercent,
            SoundStartSeconds = source.SoundStartSeconds,
            SoundEndSeconds = source.SoundEndSeconds,
            AudioOutputDeviceId = source.AudioOutputDeviceId
        };
}
