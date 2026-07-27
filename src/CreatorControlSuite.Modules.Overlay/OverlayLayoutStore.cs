using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayLayoutStore : IOverlayLayoutStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex SafeId = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    private readonly string _layoutsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OverlayLayoutStore(ISettingsStore settingsStore)
        : this(ResolveDefaultRoot(settingsStore))
    {
    }

    public OverlayLayoutStore(string layoutsRoot)
    {
        _layoutsRoot = Path.GetFullPath(layoutsRoot);
        Directory.CreateDirectory(_layoutsRoot);
    }

    public string GetLayoutFilePath(string instanceId)
    {
        string id = NormalizeInstanceId(instanceId);
        return Path.Combine(_layoutsRoot, id + ".json");
    }

    public bool Exists(string instanceId)
    {
        string path = GetLayoutFilePath(instanceId);
        return File.Exists(path);
    }

    public IReadOnlyList<string> ListInstanceIds()
    {
        if (!Directory.Exists(_layoutsRoot))
        {
            return [];
        }

        return Directory.GetFiles(_layoutsRoot, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => !string.IsNullOrWhiteSpace(id) && SafeId.IsMatch(id!))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task DeleteAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        string path = GetLayoutFilePath(instanceId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DuplicateAsync(
        string sourceId,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        OverlayLayout layout = await LoadAsync(sourceId, cancellationToken);
        await SaveAsync(targetId, layout, cancellationToken);
    }

    public async Task<OverlayLayout> LoadAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        string path = GetLayoutFilePath(instanceId);
        if (!File.Exists(path))
        {
            return OverlayLayout.CreateDefault();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            OverlayLayout? layout = JsonSerializer.Deserialize<OverlayLayout>(json, JsonOptions);
            return layout ?? OverlayLayout.CreateDefault();
        }
        catch (JsonException)
        {
            return OverlayLayout.CreateDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string instanceId,
        OverlayLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string path = GetLayoutFilePath(instanceId);
        layout.UpdatedAt = DateTimeOffset.UtcNow;
        if (layout.Version <= 0)
        {
            layout.Version = 1;
        }

        if (layout.CanvasWidth <= 0)
        {
            layout.CanvasWidth = 1920;
        }

        if (layout.CanvasHeight <= 0)
        {
            layout.CanvasHeight = 1080;
        }

        if (!OverlayCanvasSizePresets.IsValid(layout.CanvasWidth, layout.CanvasHeight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(layout),
                "Canvas-Größe muss zwischen 320×180 und 7680×4320 liegen.");
        }

        layout.Items ??= [];

        string json = JsonSerializer.Serialize(layout, JsonOptions);
        Directory.CreateDirectory(_layoutsRoot);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, json, cancellationToken);
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
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // ignore leftover temp
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string NormalizeInstanceId(string instanceId)
    {
        string id = (instanceId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) || !SafeId.IsMatch(id))
        {
            throw new ArgumentException("Ungültige Overlay-Instanz-ID.", nameof(instanceId));
        }

        return id;
    }

    private static string ResolveDefaultRoot(ISettingsStore settingsStore)
    {
        // Synchroner Fallback-Pfad; der Store kann später über Settings den Root anpassen.
        // Primär: %LocalAppData%\CreatorControlSuite\Overlay\layouts
        _ = settingsStore;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Overlay",
            "layouts");
    }
}
