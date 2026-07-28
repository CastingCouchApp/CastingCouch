using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Modules.Overlay.Extensions;

public sealed class OverlayExtensionStore : IOverlayExtensionStore
{
    public const long MaxZipSizeBytes = 50L * 1024 * 1024;
    private const string ManifestFileName = "manifest.json";
    private const int SupportedApiVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex SlugId = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".woff2", ".woff", ".ttf", ".otf", ".svg",
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".json", ".md"
    };

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OverlayExtensionStore(ISettingsStore settingsStore)
        : this(ResolveDefaultRoot(settingsStore))
    {
    }

    public OverlayExtensionStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public async Task<OverlayExtensionPackSummary> InstallFromZipAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        MemoryStream buffered = await BufferWithLimitAsync(zipStream, cancellationToken);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true);

                ZipArchiveEntry? manifestEntry = archive.Entries.FirstOrDefault(entry =>
                    string.Equals(
                        NormalizeEntryName(entry.FullName),
                        ManifestFileName,
                        StringComparison.OrdinalIgnoreCase));
                if (manifestEntry is null)
                {
                    throw new OverlayExtensionValidationException("Paket enthält keine manifest.json.");
                }

                OverlayExtensionManifest manifest = await ReadManifestAsync(manifestEntry, cancellationToken);
                ValidateManifest(manifest);

                string tempDir = Path.Combine(_root, ".installing-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    ExtractEntries(archive, tempDir);

                    string targetDir = Path.Combine(_root, manifest.Id);
                    if (Directory.Exists(targetDir))
                    {
                        Directory.Delete(targetDir, recursive: true);
                    }

                    Directory.Move(tempDir, targetDir);
                }
                catch
                {
                    TryDeleteDirectory(tempDir);
                    throw;
                }

                return ToSummary(manifest);
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

    public async Task UninstallAsync(string packId, CancellationToken cancellationToken = default)
    {
        string id = NormalizePackId(packId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string dir = Path.Combine(_root, id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<OverlayExtensionPackSummary> ListCatalog()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var result = new List<OverlayExtensionPackSummary>();
        foreach (string dir in Directory.GetDirectories(_root))
        {
            string dirName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(dirName) || !SlugId.IsMatch(dirName))
            {
                continue;
            }

            string manifestPath = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                OverlayExtensionManifest? manifest = JsonSerializer.Deserialize<OverlayExtensionManifest>(json, JsonOptions);
                if (manifest is null)
                {
                    continue;
                }

                ValidateManifest(manifest);
                result.Add(ToSummary(manifest));
            }
            catch
            {
                // Beschädigte/ungültige Pakete werden im Katalog stillschweigend übersprungen.
            }
        }

        return result
            .OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryResolveFile(string packId, string relativePath, out string fullPath)
    {
        fullPath = "";

        string id;
        try
        {
            id = NormalizePackId(packId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        string packRoot = Path.Combine(_root, id);
        string normalized = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            return false;
        }

        string packRootFull = Path.GetFullPath(packRoot) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(packRoot, normalized));
        if (!candidate.StartsWith(packRootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public static string GuessContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".md" => "text/markdown; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static async Task<MemoryStream> BufferWithLimitAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length > MaxZipSizeBytes)
        {
            throw new OverlayExtensionValidationException(
                $"ZIP-Paket zu groß (max. {MaxZipSizeBytes / (1024 * 1024)} MB).");
        }

        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxZipSizeBytes)
            {
                await buffer.DisposeAsync();
                throw new OverlayExtensionValidationException(
                    $"ZIP-Paket zu groß (max. {MaxZipSizeBytes / (1024 * 1024)} MB).");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static async Task<OverlayExtensionManifest> ReadManifestAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        try
        {
            OverlayExtensionManifest? manifest = await JsonSerializer.DeserializeAsync<OverlayExtensionManifest>(
                stream,
                JsonOptions,
                cancellationToken);
            if (manifest is null)
            {
                throw new OverlayExtensionValidationException("manifest.json ist leer oder ungültig.");
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new OverlayExtensionValidationException("manifest.json ist kein gültiges JSON: " + exception.Message);
        }
    }

    private static void ValidateManifest(OverlayExtensionManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !SlugId.IsMatch(manifest.Id))
        {
            throw new OverlayExtensionValidationException(
                "Ungültige Pack-Id im Manifest (erlaubt: Kleinbuchstaben, Ziffern, '-').");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new OverlayExtensionValidationException("Pack-Name im Manifest fehlt.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new OverlayExtensionValidationException("Pack-Version im Manifest fehlt.");
        }

        if (manifest.ApiVersion != SupportedApiVersion)
        {
            throw new OverlayExtensionValidationException(
                $"Nicht unterstützte apiVersion {manifest.ApiVersion} (erwartet {SupportedApiVersion}).");
        }

        manifest.Widgets ??= [];
        manifest.Effects ??= [];
        manifest.Animations ??= [];
        manifest.Fonts ??= [];
        manifest.Assets ??= [];

        foreach (OverlayExtensionWidget widget in manifest.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) ||
                string.IsNullOrWhiteSpace(widget.Name) ||
                string.IsNullOrWhiteSpace(widget.Entry))
            {
                throw new OverlayExtensionValidationException(
                    "Widget-Eintrag im Manifest unvollständig (id/name/entry erforderlich).");
            }
        }

        foreach (OverlayExtensionEffect effect in manifest.Effects)
        {
            if (string.IsNullOrWhiteSpace(effect.Id) ||
                string.IsNullOrWhiteSpace(effect.Name) ||
                string.IsNullOrWhiteSpace(effect.Entry))
            {
                throw new OverlayExtensionValidationException(
                    "Effect-Eintrag im Manifest unvollständig (id/name/entry erforderlich).");
            }
        }

        foreach (OverlayExtensionAnimation animation in manifest.Animations)
        {
            if (string.IsNullOrWhiteSpace(animation.Id) ||
                string.IsNullOrWhiteSpace(animation.Name) ||
                string.IsNullOrWhiteSpace(animation.Entry))
            {
                throw new OverlayExtensionValidationException(
                    "Animation-Eintrag im Manifest unvollständig (id/name/entry erforderlich).");
            }
        }

        foreach (OverlayExtensionFont font in manifest.Fonts)
        {
            if (string.IsNullOrWhiteSpace(font.Family) || string.IsNullOrWhiteSpace(font.Src))
            {
                throw new OverlayExtensionValidationException(
                    "Font-Eintrag im Manifest unvollständig (family/src erforderlich).");
            }
        }
    }

    private static void ExtractEntries(ZipArchive archive, string destinationRoot)
    {
        string destRootFull = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizeEntryName(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.EndsWith('/'))
            {
                continue; // reine Verzeichniseinträge
            }

            if (normalized.StartsWith('/') ||
                normalized.Contains(':') ||
                normalized.Split('/').Any(segment => segment == ".."))
            {
                throw new OverlayExtensionValidationException($"Unsicherer Pfad im Paket: {entry.FullName}");
            }

            string extension = Path.GetExtension(normalized);
            if (!AllowedExtensions.Contains(extension))
            {
                throw new OverlayExtensionValidationException($"Dateityp nicht erlaubt: {entry.FullName}");
            }

            string destPath = Path.GetFullPath(Path.Combine(destinationRoot, normalized));
            if (!destPath.StartsWith(destRootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new OverlayExtensionValidationException($"Zip-Slip erkannt: {entry.FullName}");
            }

            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using Stream source = entry.Open();
            using var target = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            source.CopyTo(target);
        }
    }

    private static string NormalizeEntryName(string fullName) => (fullName ?? "").Replace('\\', '/');

    private static OverlayExtensionPackSummary ToSummary(OverlayExtensionManifest manifest) => new()
    {
        Id = manifest.Id,
        Name = manifest.Name,
        Version = manifest.Version,
        ApiVersion = manifest.ApiVersion,
        Widgets = manifest.Widgets ?? [],
        Effects = manifest.Effects ?? [],
        Animations = manifest.Animations ?? [],
        Fonts = manifest.Fonts ?? [],
        Assets = manifest.Assets ?? []
    };

    private static string NormalizePackId(string packId)
    {
        string id = (packId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) || !SlugId.IsMatch(id))
        {
            throw new ArgumentException("Ungültige Extension-Pack-Id.", nameof(packId));
        }

        return id;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort Cleanup eines fehlgeschlagenen Installationsversuchs.
        }
    }

    private static string ResolveDefaultRoot(ISettingsStore settingsStore)
    {
        _ = settingsStore;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Overlay",
            "extensions");
    }
}
