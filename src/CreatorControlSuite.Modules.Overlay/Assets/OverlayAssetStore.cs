using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Modules.Overlay.Assets;

public sealed class OverlayAssetStore : IOverlayAssetStore
{
    public const long MaxAssetSizeBytes = 15L * 1024 * 1024;
    private const string IndexFileName = "index.json";

    private static readonly Regex SafeId = new("^[a-zA-Z0-9]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<AssetRecord> _records;

    public OverlayAssetStore(ISettingsStore settingsStore)
        : this(ResolveDefaultRoot(settingsStore))
    {
    }

    public OverlayAssetStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _records = LoadIndexUnlocked();
    }

    public string RootPath => _root;

    public IReadOnlyList<OverlayAssetInfo> List()
    {
        _gate.Wait();
        try
        {
            return _records
                .Select(ToInfo)
                .Where(a => a is not null)
                .Cast<OverlayAssetInfo>()
                .OrderByDescending(a => a.CreatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryGet(string id, out OverlayAssetInfo asset)
    {
        asset = null!;
        string normalized = NormalizeId(id);
        if (normalized.Length == 0)
        {
            return false;
        }

        _gate.Wait();
        try
        {
            AssetRecord? record = _records.FirstOrDefault(r =>
                string.Equals(r.Id, normalized, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return false;
            }

            OverlayAssetInfo? info = ToInfo(record);
            if (info is null)
            {
                return false;
            }

            asset = info;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OverlayAssetInfo> ImportAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string originalName = Path.GetFileName(fileName?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(originalName))
        {
            throw new OverlayAssetValidationException("Dateiname fehlt.");
        }

        string ext = Path.GetExtension(originalName);
        if (!AllowedExtensions.Contains(ext))
        {
            throw new OverlayAssetValidationException(
                "Dateityp nicht erlaubt. Erlaubt: png, jpg, jpeg, webp, gif, bmp, svg.");
        }

        MemoryStream buffered = await BufferWithLimitAsync(content, cancellationToken);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                string id = Guid.NewGuid().ToString("N");
                string storedName = id + ext.ToLowerInvariant();
                string fullPath = Path.Combine(_root, storedName);

                buffered.Position = 0;
                await using (FileStream fs = new(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await buffered.CopyToAsync(fs, cancellationToken);
                }

                var record = new AssetRecord
                {
                    Id = id,
                    FileName = storedName,
                    OriginalName = originalName,
                    ContentType = GuessContentType(ext),
                    SizeBytes = buffered.Length,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _records.Add(record);
                await SaveIndexUnlockedAsync(cancellationToken);

                return ToInfo(record)!;
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            await buffered.DisposeAsync();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeId(id);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Ungültige Asset-ID.", nameof(id));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            AssetRecord? record = _records.FirstOrDefault(r =>
                string.Equals(r.Id, normalized, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return;
            }

            _records.Remove(record);
            await SaveIndexUnlockedAsync(cancellationToken);

            string path = Path.Combine(_root, record.FileName);
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

    public static string GuessContentType(string extensionOrPath)
    {
        string ext = extensionOrPath.Contains('.', StringComparison.Ordinal)
            ? Path.GetExtension(extensionOrPath)
            : extensionOrPath;
        return ext.ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private OverlayAssetInfo? ToInfo(AssetRecord record)
    {
        string path = Path.Combine(_root, record.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return new OverlayAssetInfo
        {
            Id = record.Id,
            FileName = record.FileName,
            OriginalName = record.OriginalName,
            ContentType = string.IsNullOrWhiteSpace(record.ContentType)
                ? GuessContentType(record.FileName)
                : record.ContentType,
            SizeBytes = record.SizeBytes > 0 ? record.SizeBytes : new FileInfo(path).Length,
            CreatedAt = record.CreatedAt,
            LocalPath = path,
            PublicUrl = "/assets/" + record.Id
        };
    }

    private List<AssetRecord> LoadIndexUnlocked()
    {
        string indexPath = Path.Combine(_root, IndexFileName);
        if (!File.Exists(indexPath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(indexPath);
            AssetIndex? index = JsonSerializer.Deserialize<AssetIndex>(json, JsonOptions);
            return index?.Assets?.ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveIndexUnlockedAsync(CancellationToken cancellationToken)
    {
        string indexPath = Path.Combine(_root, IndexFileName);
        string tempPath = indexPath + ".tmp";
        var index = new AssetIndex { Assets = _records.ToList() };
        await using (FileStream fs = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, index, JsonOptions, cancellationToken);
        }

        File.Copy(tempPath, indexPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static async Task<MemoryStream> BufferWithLimitAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length > MaxAssetSizeBytes)
        {
            throw new OverlayAssetValidationException(
                $"Datei zu groß (max. {MaxAssetSizeBytes / (1024 * 1024)} MB).");
        }

        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxAssetSizeBytes)
            {
                await buffer.DisposeAsync();
                throw new OverlayAssetValidationException(
                    $"Datei zu groß (max. {MaxAssetSizeBytes / (1024 * 1024)} MB).");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static string NormalizeId(string? id)
    {
        string value = (id ?? "").Trim();
        if (value.Length == 0 || !SafeId.IsMatch(value))
        {
            return "";
        }

        return value;
    }

    private static string ResolveDefaultRoot(ISettingsStore settingsStore)
    {
        _ = settingsStore;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Overlay",
            "assets");
    }

    private sealed class AssetIndex
    {
        public List<AssetRecord> Assets { get; set; } = [];
    }

    private sealed class AssetRecord
    {
        public string Id { get; set; } = "";
        public string FileName { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
