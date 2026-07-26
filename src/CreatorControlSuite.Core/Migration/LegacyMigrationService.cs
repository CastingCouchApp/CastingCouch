using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Migration;

public sealed class LegacyMigrationService : ILegacyMigrationService
{
    private readonly ISettingsStore _settingsStore;

    public LegacyMigrationService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public Task<IReadOnlyList<MigrationCandidate>> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<MigrationCandidate>();

        var roots = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
                "StreamingSuite"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamingSuite"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "StreamingSuite")
        };

        foreach (var root in roots.Distinct())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var detected = new List<string>();

            if (File.Exists(Path.Combine(root, "settings.json")))
            {
                detected.Add("Einstellungen");
            }

            if (File.Exists(Path.Combine(root, "overlay-data.json")))
            {
                detected.Add("Overlay-Daten");
            }

            if (Directory.Exists(Path.Combine(root, "content")))
            {
                detected.Add("Overlay-Inhalte");
            }

            if (Directory.Exists(Path.Combine(root, "alerts")))
            {
                detected.Add("Alert-Dateien");
            }

            candidates.Add(
                new MigrationCandidate(
                    "LegacyStreamingSuite",
                    root,
                    "Bisherige Streaming Suite",
                    detected));
        }

        return Task.FromResult<IReadOnlyList<MigrationCandidate>>(
            candidates);
    }

    public async Task<MigrationResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(sourcePath);
        }

        var imported = new List<string>();
        var warnings = new List<string>();
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        var legacySettingsPath = Path.Combine(
            sourcePath,
            "settings.json");

        if (File.Exists(legacySettingsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(
                    await File.ReadAllTextAsync(
                        legacySettingsPath,
                        cancellationToken));

                var root = document.RootElement;

                TryReadString(
                    root,
                    "obsHost",
                    value => settings.Obs.Host = value);

                TryReadInt(
                    root,
                    "obsPort",
                    value => settings.Obs.Port = value);

                TryReadString(
                    root,
                    "twitchChannel",
                    value =>
                    {
                        settings.Twitch.ChannelName = value;
                        settings.Branding.ChannelName = value;
                    });

                TryReadString(
                    root,
                    "overlayRoot",
                    value => settings.Overlay.RootPath = value);

                TryReadString(
                    root,
                    "startScene",
                    value => settings.Obs.StartScene = value);

                TryReadString(
                    root,
                    "liveScene",
                    value => settings.Obs.LiveScene = value);

                TryReadString(
                    root,
                    "pauseScene",
                    value => settings.Obs.PauseScene = value);

                TryReadString(
                    root,
                    "endScene",
                    value => settings.Obs.EndScene = value);

                TryReadInt(
                    root,
                    "endSceneSeconds",
                    value => settings.Workflow.EndSceneSeconds = value);

                imported.Add("Einstellungen");
            }
            catch (Exception exception)
            {
                warnings.Add(
                    "Legacy-Einstellungen konnten nicht vollständig gelesen werden: " +
                    exception.Message);
            }
        }

        var legacyOverlayRoot = Path.Combine(
            sourcePath,
            "content");

        if (Directory.Exists(legacyOverlayRoot))
        {
            settings.Overlay.RootPath =
                legacyOverlayRoot;

            settings.Overlay.UseBundledOverlay = false;
            imported.Add("Overlay-Pfad");
        }

        var alertsPath = Path.Combine(
            sourcePath,
            "alerts");

        if (Directory.Exists(alertsPath))
        {
            imported.Add("Alert-Dateien erkannt");
            warnings.Add(
                "Alert-Dateien wurden erkannt. " +
                "Die Zuordnung zu Alert-Typen muss einmalig geprüft werden.");
        }

        await _settingsStore.SaveAsync(
            settings,
            cancellationToken);

        return new MigrationResult(
            true,
            sourcePath,
            imported,
            warnings,
            imported.Count == 0
                ? "Keine importierbaren Daten gefunden."
                : "Migration abgeschlossen.");
    }

    private static void TryReadString(
        JsonElement root,
        string propertyName,
        Action<string> setter)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();

            if (!string.IsNullOrWhiteSpace(text))
            {
                setter(text);
            }
        }
    }

    private static void TryReadInt(
        JsonElement root,
        string propertyName,
        Action<int> setter)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.TryGetInt32(out var number))
        {
            setter(number);
        }
    }
}
