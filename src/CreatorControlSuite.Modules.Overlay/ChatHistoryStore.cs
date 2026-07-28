using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class ChatHistoryStore : IChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ISettingsStore? _settingsStore;
    private readonly string? _fixedPath;
    private readonly object _gate = new();

    public ChatHistoryStore(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public ChatHistoryStore(string filePath)
    {
        _fixedPath = filePath;
    }

    public string FilePath =>
        _fixedPath ?? ResolvePath(
            _settingsStore?.LoadAsync().GetAwaiter().GetResult().Overlay
            ?? new OverlaySettings());

    public async Task<IReadOnlyList<OverlayRealtimeEvent>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        string path = await ResolveFilePathAsync(cancellationToken);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            List<OverlayRealtimeEvent>? events =
                await JsonSerializer.DeserializeAsync<List<OverlayRealtimeEvent>>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            return events ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<OverlayRealtimeEvent> events,
        CancellationToken cancellationToken = default)
    {
        string path = await ResolveFilePathAsync(cancellationToken);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                events ?? Array.Empty<OverlayRealtimeEvent>(),
                JsonOptions,
                cancellationToken);
        }

        lock (_gate)
        {
            File.Copy(tempPath, path, overwrite: true);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }
    }

    public static string ResolvePath(OverlaySettings overlay)
    {
        string root = (overlay.RootPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CreatorControlSuite",
                "Overlay");
        }

        return Path.Combine(root, "chat-history.json");
    }

    private async Task<string> ResolveFilePathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_fixedPath))
        {
            return _fixedPath;
        }

        AppSettings settings = await _settingsStore!.LoadAsync(cancellationToken);
        return ResolvePath(settings.Overlay);
    }
}
