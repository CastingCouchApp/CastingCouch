using System.Text.Json;
using System.Text.Json.Nodes;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayDataService : IOverlayDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISettingsStore _settingsStore;
    private readonly object _stateLock = new();
    private OverlayData _current = new();

    public OverlayDataService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public OverlayData Current
    {
        get
        {
            lock (_stateLock)
            {
                return Clone(_current);
            }
        }
    }

    public event EventHandler<OverlayData>? DataChanged;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        lock (_stateLock)
        {
            _current.Branding.DisplayName =
                settings.Branding.DisplayName;
            _current.Branding.ChannelName =
                settings.Branding.ChannelName;
            _current.Branding.AccentColor =
                settings.Branding.AccentColor;
            _current.Branding.LogoPath =
                settings.Branding.LogoPath;
        }

        if (settings.Overlay.UseBundledOverlay)
        {
            await InstallBundledOverlayAsync(cancellationToken);
        }

        await WriteAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Action<OverlayData> update,
        CancellationToken cancellationToken = default)
    {
        OverlayData snapshot;

        lock (_stateLock)
        {
            update(_current);
            _current.UpdatedAt = DateTimeOffset.UtcNow;
            snapshot = Clone(_current);
        }

        await WriteAsync(cancellationToken);
        DataChanged?.Invoke(this, snapshot);
    }

    public async Task WriteAsync(
        CancellationToken cancellationToken = default)
    {
        await OverlayDataWriteCoordinator.Lock.WaitAsync(cancellationToken);

        try
        {
            var primaryPath = await GetDataFilePathAsync(cancellationToken);
            OverlayData snapshot;

            lock (_stateLock)
            {
                snapshot = Clone(_current);
            }

            // Die Suite besitzt genau eine physische overlay-data.json.
            // Importierte Overlay-Projekte greifen über einen Dateiverweis
            // auf diese zentrale Datei zu; hier wird daher nichts gespiegelt.
            await WriteSnapshotAsync(
                Path.GetFullPath(primaryPath),
                snapshot,
                cancellationToken);
        }
        finally
        {
            OverlayDataWriteCoordinator.Lock.Release();
        }
    }


    private static async Task WriteSnapshotAsync(
        string path,
        OverlayData snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject output;
        if (File.Exists(path))
        {
            try
            {
                var existingText = await File.ReadAllTextAsync(path, cancellationToken);
                output = JsonNode.Parse(existingText) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                // Eine defekte oder leere Datei wird durch eine gültige Datenstruktur ersetzt.
                output = new JsonObject();
            }
        }
        else
        {
            output = new JsonObject();
        }

        // Vorhandene unbekannte Felder (z. B. Layout-Konfiguration des Nutzers)
        // bleiben erhalten. Nur die von der Suite verwalteten Datenbereiche werden
        // mit dem aktuellen Zustand fortgeschrieben.
        var managed = JsonSerializer.SerializeToNode(snapshot, JsonOptions) as JsonObject ?? new JsonObject();

        // Spotify-Laufzeitdaten haben genau einen Besitzer: den dedizierten
        // Spotify-Schreiber in MainWindow. Der allgemeine OverlayDataService
        // wird auch durch OBS-, Twitch-, Szenen- und Browserquellen-Refreshes
        // aufgerufen. Sein interner Standardzustand darf deshalb niemals den
        // bereits gültigen Spotify-Unterbaum mit connected=false und leeren
        // Titeldaten überschreiben.
        managed.Remove("spotify");

        // Der komplette Stream-Laufzeitzustand hat ebenfalls genau einen Besitzer:
        // die OBS-Streamüberwachung in MainWindow. Workflow-, Remote- und
        // Vorbereitungsdienste dürfen Phase, Countdown oder Standardwerte nicht
        // mehr über isLive/startedAt schreiben. Dadurch kann die Unterstützung
        // eines zweiten Rechners den lokal bestätigten Live-Status nicht mehr
        // auf OFFLINE zurücksetzen.
        managed.Remove("stream");

        foreach (var property in managed)
        {
            output[property.Key] = property.Value?.DeepClone();
        }

        var json = output.ToJsonString(JsonOptions);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllTextAsync(temp, json, cancellationToken);

            // WICHTIG: Die Overlay-Projekte können über Hardlinks mit dieser Datei
            // verbunden sein. Ein File.Move(..., overwrite: true) ersetzt den
            // Dateieintrag und trennt dadurch sämtliche Hardlinks von der neuen
            // Datei. Danach lesen verschiedene OBS-Quellen unterschiedliche
            // Datenstände. Deshalb wird der bestehende Dateiknoten jetzt bewusst
            // in-place überschrieben. So bleiben Hardlinks und Symlinks erhalten.
            await using var source = new FileStream(
                temp,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);

            await using var destination = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                useAsync: true);

            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Eine liegengebliebene temporäre Datei darf den Overlay-Betrieb
                // niemals beeinflussen.
            }
        }
    }

    public async Task<string> GetDataFilePathAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            await GetOverlayRootAsync(cancellationToken)));

        var denverUiPath = Path.Combine(root, "Overlay", "modules", "ui");
        var denverDataPath = Path.Combine(root, "Overlay", "data", "overlay-data.json");

        // DenverJohn v18.x: Alle UI-Module liegen unter Overlay/modules/ui und
        // laden ../../data/overlay-data.json. Damit ist Overlay/data zwingend
        // die einzige autoritative Laufzeitdatei – auch wenn eine alte Einstellung
        // noch auf <Root>/data/overlay-data.json zeigt.
        if (Directory.Exists(denverUiPath) &&
            (File.Exists(Path.Combine(denverUiPath, "spotify.html")) ||
             File.Exists(Path.Combine(denverUiPath, "live-status.html"))))
        {
            return denverDataPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.Overlay.DataFilePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                settings.Overlay.DataFilePath.Trim()));
        }

        var nestedPath = Path.Combine(root, "Overlay", "data", settings.Overlay.DataFileName);
        if (File.Exists(nestedPath)) return nestedPath;

        return Path.Combine(root, "data", settings.Overlay.DataFileName);
    }

    public async Task<string> GetOverlayRootAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Overlay.RootPath))
        {
            return settings.Overlay.RootPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Overlay");
    }

    public async Task InstallBundledOverlayAsync(
        CancellationToken cancellationToken = default)
    {
        var root = await GetOverlayRootAsync(cancellationToken);

        foreach (var directory in new[]
        {
            root,
            Path.Combine(root, "data"),
            Path.Combine(root, "assets"),
            Path.Combine(root, "modules"),
            Path.Combine(root, "scenes")
        })
        {
            Directory.CreateDirectory(directory);
        }

        var bundledRoot = Path.Combine(
            AppContext.BaseDirectory,
            "BundledOverlay");

        if (Directory.Exists(bundledRoot))
        {
            CopyDirectory(
                bundledRoot,
                root);
        }
    }

    private static void CopyDirectory(
        string source,
        string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(
                    target,
                    Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(
                    target,
                    Path.GetFileName(directory)));
        }
    }

    private static OverlayData Clone(OverlayData value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        return JsonSerializer.Deserialize<OverlayData>(
                   json,
                   JsonOptions)
               ?? new OverlayData();
    }
}
