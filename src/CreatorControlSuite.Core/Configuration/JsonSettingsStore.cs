using System.Text.Json;
using System.Text.Json.Nodes;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Core.Configuration;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path = path;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        JsonObject root;
        await using (FileStream stream = File.OpenRead(_path))
        {
            root = await JsonSerializer.DeserializeAsync<JsonObject>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? new JsonObject();
        }

        bool migrated = SettingsSchemaMigrator.Migrate(root);
        AppSettings settings = EnsureDefaults(
            root.Deserialize<AppSettings>(SerializerOptions) ?? new AppSettings());
        if (migrated)
        {
            await SaveAsync(settings, cancellationToken);
        }

        return settings;
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureDefaults(settings);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        await _saveLock.WaitAsync(cancellationToken);
        string? tempPath = null;

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Every save receives its own temporary file. Together with the
            // semaphore this prevents concurrent startup events from moving or
            // deleting another save operation's settings.json.tmp file.
            string operationTempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            tempPath = operationTempPath;

            await using (var stream = new FileStream(
                             operationTempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_path))
            {
                string backupPath = _path + ".bak";
                File.Copy(_path, backupPath, overwrite: true);
            }

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.Move(operationTempPath, _path, overwrite: true);
                    tempPath = null;
                    return;
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }

            // Last-resort fallback if antivirus or another process repeatedly
            // blocks the atomic rename. The temporary file still belongs only
            // to this save operation.
            await using FileStream source = File.OpenRead(operationTempPath);
            await using var destination = new FileStream(
                             _path,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.Read,
                             bufferSize: 4096,
                             useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Cleanup must never replace the original save exception.
                }
            }

            _saveLock.Release();
        }
    }

    private static AppSettings EnsureDefaults(AppSettings settings)
    {
        settings.Product ??= new ProductSettings();
        settings.General ??= new GeneralSettings();
        settings.Branding ??= new BrandingSettings();
        settings.Obs ??= new ObsSettings();
        settings.Twitch ??= new TwitchSettings();
        settings.Spotify ??= new SpotifySettings();
        settings.MusicPlayer ??= new MusicPlayerSettings();
        settings.YouTubeMusic ??= new YouTubeMusicSettings();
        settings.StreamerBot ??= new StreamerBotSettings();
        settings.Alerts ??= new AlertSettings();
        settings.Overlay ??= new OverlaySettings();
        settings.Overlay.Chat ??= new OverlayChatSettings();
        settings.Workflow ??= new WorkflowSettings();
        settings.StreamDeck ??= new StreamDeckSettings();
        settings.Dashboard ??= new DashboardSettings();
        settings.Updates ??= new UpdateSettings();

        settings.Overlay.EnsureInstancesMigrated();

        if (string.IsNullOrWhiteSpace(settings.MusicPlayer.ProviderId))
        {
            settings.MusicPlayer.ProviderId = MusicProviderIds.Spotify;
        }
        else
        {
            settings.MusicPlayer.ProviderId = MusicProviderIds.Normalize(settings.MusicPlayer.ProviderId);
        }

        if (settings.YouTubeMusic.BridgePort is <= 0 or > 65535)
        {
            settings.YouTubeMusic.BridgePort = 43831;
        }

        if (settings.YouTubeMusic.StateTimeoutSeconds is <= 0)
        {
            settings.YouTubeMusic.StateTimeoutSeconds = 12;
        }

        return settings;
    }
}
