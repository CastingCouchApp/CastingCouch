using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Updates;

public sealed class LocalUpdateService : IUpdateService
{
    public const string DefaultGitHubOwner = "frankhildebrandt";
    public const string DefaultGitHubRepo = "CreatorControlSuite";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly IUpdateSignatureVerifier _signatureVerifier;
    private readonly string _dataRoot;
    private readonly string _backupRoot;
    private readonly string _downloadRoot;
    private readonly string _gitHubOwner;
    private readonly string _gitHubRepo;
    private readonly Func<string> _currentVersionProvider;
    private readonly string _installDirectory;
    private readonly string _mainExeName;
    private readonly string _updaterExeName;

    public LocalUpdateService(
        HttpClient httpClient,
        ISettingsStore settingsStore,
        IUpdateSignatureVerifier signatureVerifier,
        string dataRoot,
        string? gitHubOwner = null,
        string? gitHubRepo = null,
        Func<string>? currentVersionProvider = null,
        string? installDirectory = null,
        string mainExeName = "CreatorControlSuite.exe",
        string updaterExeName = "CreatorControlSuite.Updater.exe")
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _signatureVerifier = signatureVerifier;
        _dataRoot = dataRoot;
        _backupRoot = Path.Combine(dataRoot, "Backups");
        _downloadRoot = Path.Combine(dataRoot, "Downloads");
        _gitHubOwner = string.IsNullOrWhiteSpace(gitHubOwner)
            ? DefaultGitHubOwner
            : gitHubOwner.Trim();
        _gitHubRepo = string.IsNullOrWhiteSpace(gitHubRepo)
            ? DefaultGitHubRepo
            : gitHubRepo.Trim();
        _currentVersionProvider = currentVersionProvider ?? GetAssemblyVersion;
        _installDirectory = string.IsNullOrWhiteSpace(installDirectory)
            ? AppContext.BaseDirectory
            : installDirectory;
        _mainExeName = mainExeName;
        _updaterExeName = updaterExeName;

        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_downloadRoot);

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CreatorControlSuite", "1.0"));
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        string currentVersion = _currentVersionProvider();
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        string channel = NormalizeChannel(
            settings.Updates.Channel,
            settings.Product.UpdateChannel);

        try
        {
            List<GitHubRelease> releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(
                $"https://api.github.com/repos/{_gitHubOwner}/{_gitHubRepo}/releases?per_page=30",
                JsonOptions,
                cancellationToken) ?? [];

            GitHubRelease? release = SelectRelease(releases, channel);
            if (release is null)
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    $"Kein GitHub-Release für Kanal {channel} gefunden.");
            }

            GitHubAsset? manifestAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(
                    asset.Name,
                    "update-manifest.json",
                    StringComparison.OrdinalIgnoreCase));

            if (manifestAsset is null ||
                string.IsNullOrWhiteSpace(manifestAsset.BrowserDownloadUrl))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    $"Release {release.TagName} enthält kein update-manifest.json.");
            }

            using HttpResponseMessage manifestResponse = await _httpClient.GetAsync(
                manifestAsset.BrowserDownloadUrl,
                cancellationToken);
            manifestResponse.EnsureSuccessStatusCode();

            await using Stream manifestStream =
                await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
            SignedUpdateManifest? manifest = await JsonSerializer.DeserializeAsync<SignedUpdateManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken);

            if (manifest is null)
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    "Update-Manifest konnte nicht gelesen werden.");
            }

            if (!string.Equals(
                    manifest.ProductId,
                    UpdateManifestCanonical.ProductId,
                    StringComparison.Ordinal))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    "Update-Manifest hat eine ungültige ProductId.");
            }

            if (!_signatureVerifier.VerifyManifest(manifest))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    "Update-Manifest-Signatur ist ungültig.");
            }

            GitHubAsset? packageAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(
                    asset.Name,
                    manifest.PackageFileName,
                    StringComparison.OrdinalIgnoreCase));

            if (packageAsset is null ||
                string.IsNullOrWhiteSpace(packageAsset.BrowserDownloadUrl))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    $"Paket {manifest.PackageFileName} fehlt im Release.");
            }

            if (!ProductVersionInfo.TryParse(currentVersion, out ProductVersionInfo? current) ||
                !ProductVersionInfo.TryParse(manifest.Version, out ProductVersionInfo? candidate))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    "Versionsvergleich fehlgeschlagen.");
            }

            if (candidate <= current)
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    $"Aktuelle Version {currentVersion} ist aktuell ({channel}).");
            }

            if (!string.IsNullOrWhiteSpace(manifest.MinimumVersion) &&
                ProductVersionInfo.TryParse(manifest.MinimumVersion, out ProductVersionInfo? minimum) &&
                current < minimum)
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    $"Update {manifest.Version} erfordert mindestens Version {manifest.MinimumVersion}.");
            }

            var package = new UpdatePackage(
                manifest.Version,
                manifest.Channel,
                new Uri(packageAsset.BrowserDownloadUrl),
                manifest.PackageSha256,
                manifest.PackageSizeBytes,
                string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                    ? release.Body ?? string.Empty
                    : manifest.ReleaseNotes,
                Mandatory: false,
                manifest);

            return new UpdateCheckResult(
                true,
                currentVersion,
                package,
                $"Update {manifest.Version} verfügbar.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new UpdateCheckResult(
                false,
                currentVersion,
                null,
                "Updateprüfung fehlgeschlagen: " + exception.Message);
        }
    }

    public async Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        string target = Path.Combine(
            _downloadRoot,
            $"CreatorControlSuite-{package.Version}.zip");

        using HttpResponseMessage response = await _httpClient.GetAsync(
            package.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength
                    ?? package.SizeBytes;

        await using Stream source = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        await using (FileStream destination = File.Create(target))
        {
            byte[] buffer = new byte[128 * 1024];
            long written = 0;

            while (true)
            {
                int count = await source.ReadAsync(buffer, cancellationToken);
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
                    progress?.Report(Math.Min(1, written / (double)total));
                }
            }

            await destination.FlushAsync(cancellationToken);
        }

        string hash = await ComputeSha256Async(target, cancellationToken);
        if (!string.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidOperationException(
                "Updatepaket hat eine ungültige SHA-256-Prüfsumme.");
        }

        if (package.Manifest is not null)
        {
            bool packageOk = await _signatureVerifier.VerifyPackageAsync(
                target,
                package.Manifest,
                cancellationToken);

            if (!packageOk)
            {
                File.Delete(target);
                throw new InvalidOperationException(
                    "Updatepaket entspricht nicht dem signierten Manifest.");
            }
        }

        return target;
    }

    public Task ApplyAsync(
        string packageZipPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(packageZipPath))
        {
            throw new FileNotFoundException(
                "Updatepaket wurde nicht gefunden.",
                packageZipPath);
        }

        string updaterPath = Path.Combine(_installDirectory, _updaterExeName);
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException(
                "Updater wurde nicht gefunden.",
                updaterPath);
        }

        string installDir = Path.GetFullPath(_installDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        try
        {
            string probe = Path.Combine(installDir, $".ccs-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Keine Schreibrechte im Installationsordner. " +
                "Bitte die App als Administrator starten oder erneut mit dem MSI installieren.");
        }
        catch (IOException exception) when (
            exception.Message.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Keine Schreibrechte im Installationsordner. " +
                "Bitte die App als Administrator starten oder erneut mit dem MSI installieren.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = installDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(Path.GetFullPath(packageZipPath));
        startInfo.ArgumentList.Add(installDir);
        startInfo.ArgumentList.Add(_mainExeName);
        startInfo.ArgumentList.Add(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Updater konnte nicht gestartet werden.");
        }

        return Task.CompletedTask;
    }

    public async Task<UpdateBackup> CreateBackupAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        string id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(
            _backupRoot,
            $"backup-{id}-{currentVersion}.zip");

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

        foreach (string file in EnumerateBackupFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = Path.GetRelativePath(_dataRoot, file);
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
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<UpdateBackup> results =
            [.. Directory.GetFiles(_backupRoot, "backup-*.zip")
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
                .OrderByDescending(item => item.CreatedAt)];

        return Task.FromResult(results);
    }

    public async Task RestoreBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UpdateBackup> backups = await ListBackupsAsync(cancellationToken);

        UpdateBackup backup = backups.FirstOrDefault(item =>
                item.Id.Contains(backupId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Backup wurde nicht gefunden.");

        using ZipArchive archive = ZipFile.OpenRead(backup.Path);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destination = Path.Combine(_dataRoot, entry.FullName);
            string? directory = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    internal static GitHubRelease? SelectRelease(
        IReadOnlyList<GitHubRelease> releases,
        string channel)
    {
        string normalized = NormalizeChannel(channel, null);

        foreach (GitHubRelease? release in releases.Where(item => !item.Draft))
        {
            if (MatchesChannel(release, normalized))
            {
                return release;
            }
        }

        return null;
    }

    internal static bool MatchesChannel(GitHubRelease release, string channel)
    {
        string tag = release.TagName ?? string.Empty;
        string name = release.Name ?? string.Empty;
        string haystack = $"{tag} {name}";

        return channel switch
        {
            "Stable" => !release.Prerelease,
            "Beta" => release.Prerelease &&
                      (haystack.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
                       !haystack.Contains("alpha", StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static string NormalizeChannel(string? primary, string? fallback)
    {
        string? value = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Alpha";
        }

        return value.Trim() switch
        {
            var item when item.Equals("stable", StringComparison.OrdinalIgnoreCase) => "Stable",
            var item when item.Equals("beta", StringComparison.OrdinalIgnoreCase) => "Beta",
            _ => "Alpha"
        };
    }

    private IEnumerable<string> EnumerateBackupFiles()
    {
        foreach (string? name in new[] { "settings.json" })
        {
            string path = Path.Combine(_dataRoot, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (string? directoryName in new[] { "Profiles", "Overlay", "Secrets" })
        {
            string path = Path.Combine(_dataRoot, directoryName);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(
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
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string GetAssemblyVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    internal sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
