using System.IO.Compression;
using System.Security.Cryptography;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Updates;

public sealed class LocalUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly string _dataRoot;
    private readonly string _backupRoot;
    private readonly string _downloadRoot;

    public LocalUpdateService(
        HttpClient httpClient,
        ISettingsStore settingsStore,
        string dataRoot)
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _dataRoot = dataRoot;
        _backupRoot = Path.Combine(dataRoot, "Backups");
        _downloadRoot = Path.Combine(dataRoot, "Downloads");

        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_downloadRoot);
    }

    public Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            new UpdateCheckResult(
                UpdateAvailable: false,
                CurrentVersion: "2.0.81",
                Package: null,
                Detail:
                    "Updatequelle ist vorbereitet. " +
                    "Ein signiertes Release-Manifest wird vor der Beta ergänzt."));
    }

    public async Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(
            _downloadRoot,
            $"CreatorControlSuite-{package.Version}.zip");

        using var response = await _httpClient.GetAsync(
            package.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength
                    ?? package.SizeBytes;

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        await using var destination = File.Create(target);

        var buffer = new byte[128 * 1024];
        long written = 0;

        while (true)
        {
            var count = await source.ReadAsync(
                buffer,
                cancellationToken);

            if (count == 0)
            {
                break;
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, count),
                cancellationToken);

            written += count;

            if (total > 0)
            {
                progress?.Report(
                    Math.Min(1, written / (double)total));
            }
        }

        await destination.FlushAsync(cancellationToken);

        var hash = await ComputeSha256Async(
            target,
            cancellationToken);

        if (!string.Equals(
                hash,
                package.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);

            throw new InvalidOperationException(
                "Updatepaket hat eine ungültige SHA-256-Prüfsumme.");
        }

        return target;
    }

    public async Task<UpdateBackup> CreateBackupAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(
            _backupRoot,
            $"backup-{id}-{currentVersion}.zip");

        using var archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);

        foreach (var file in EnumerateBackupFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(
                _dataRoot,
                file);

            archive.CreateEntryFromFile(
                file,
                relative,
                CompressionLevel.Optimal);
        }

        var info = new FileInfo(path);

        return new UpdateBackup(
            id,
            currentVersion,
            path,
            DateTimeOffset.Now,
            info.Length);
    }

    public Task<IReadOnlyList<UpdateBackup>> ListBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UpdateBackup> results =
            Directory.GetFiles(_backupRoot, "backup-*.zip")
                .Select(path =>
                {
                    var info = new FileInfo(path);

                    return new UpdateBackup(
                        Path.GetFileNameWithoutExtension(path),
                        "unbekannt",
                        path,
                        info.CreationTimeUtc,
                        info.Length);
                })
                .OrderByDescending(item => item.CreatedAt)
                .ToList();

        return Task.FromResult(results);
    }

    public async Task RestoreBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var backups = await ListBackupsAsync(cancellationToken);

        var backup = backups.FirstOrDefault(item =>
            item.Id.Contains(
                backupId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Backup wurde nicht gefunden.");

        using var archive = ZipFile.OpenRead(backup.Path);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.Combine(
                _dataRoot,
                entry.FullName);

            var directory = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            entry.ExtractToFile(
                destination,
                overwrite: true);
        }
    }

    private IEnumerable<string> EnumerateBackupFiles()
    {
        foreach (var name in new[]
        {
            "settings.json"
        })
        {
            var path = Path.Combine(_dataRoot, name);

            if (File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (var directoryName in new[]
        {
            "Profiles",
            "Overlay",
            "Secrets"
        })
        {
            var path = Path.Combine(_dataRoot, directoryName);

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }
}
