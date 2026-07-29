using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class LocalUpdateServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Constructor_UsesGitHubDefaults_AndPreservesExistingUserAgent()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var settings = new MemorySettingsStore(new AppSettings());
        var verifier = new StubSignatureVerifier();

        _ = new LocalUpdateService(
            client,
            settings,
            verifier,
            directory.Path,
            gitHubOwner: "  ",
            gitHubRepo: null,
            currentVersionProvider: () => "1.0.0",
            installDirectory: "  ");

        Assert.Contains(
            client.DefaultRequestHeaders.UserAgent,
            item => item.Product?.Name == "CreatorControlSuite");

        using var second = new HttpClient(handler);
        second.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Existing", "2.0"));
        _ = new LocalUpdateService(
            second,
            settings,
            verifier,
            directory.Path,
            gitHubOwner: "Owner",
            gitHubRepo: "Repo",
            currentVersionProvider: () => "1.0.0",
            installDirectory: directory.Path);
        Assert.Single(second.DefaultRequestHeaders.UserAgent);
    }

    [Fact]
    public async Task CheckAsync_ReturnsAvailableUpdate_ForNewerSignedManifest()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            releaseNotes: "notes",
            packageFileName: "pkg.zip");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.True(result.UpdateAvailable);
        Assert.Equal("8.0.0-alpha2", result.Package!.Version);
        Assert.Equal("notes", result.Package.ReleaseNotes);
        Assert.Contains(
            "CastingCouchApp/CastingCouch",
            fixture.Handler.Requests[0].Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_UsesReleaseBody_WhenManifestNotesEmpty()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            releaseNotes: "  ",
            releaseBody: "from-body",
            packageFileName: "pkg.zip");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.True(result.UpdateAvailable);
        Assert.Equal("from-body", result.Package!.ReleaseNotes);
    }

    [Fact]
    public async Task CheckAsync_ReportsMissingReleaseForChannel()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler(JsonResponse("[]"));
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            channel: "Stable",
            currentVersion: "8.0.0");

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("Kein GitHub-Release", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsManifestAssetWithEmptyUrl()
    {
        using var directory = new TemporaryDirectory();
        string releases = ReleasesJson(
            tag: "v8.0.0-alpha2",
            prerelease: true,
            assets: [new ReleaseAsset("update-manifest.json", "  ")]);
        using var handler = new QueueHandler(JsonResponse(releases));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("update-manifest.json", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsPackageAssetWithEmptyUrl()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            packageFileName: "pkg.zip",
            packageDownloadUrl: " ");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("fehlt im Release", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_IgnoresBlankMinimumVersion()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            minimumVersion: "  ");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.True(result.UpdateAvailable);
    }

    [Fact]
    public async Task ListBackupsAsync_PropagatesCancellation()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ListBackupsAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task CheckAsync_ReportsUnreadableManifest()
    {
        using var directory = new TemporaryDirectory();
        string releases = ReleasesJson(
            tag: "v8.0.0-alpha2",
            prerelease: true,
            assets:
            [
                new ReleaseAsset(
                    "update-manifest.json",
                    "https://example.test/update-manifest.json")
            ]);
        using var handler = new QueueHandler(
            JsonResponse(releases),
            JsonResponse("null"));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("nicht gelesen", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsInvalidProductId()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            productId: "WrongProduct");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("ProductId", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsInvalidSignature()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            signatureValid: false);

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("Signatur", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsMissingPackageAsset()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha2",
            packageFileName: "missing.zip",
            includePackageAsset: false);

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("fehlt im Release", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsVersionParseFailure()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "not-a-version",
            manifestVersion: "8.0.0-alpha2");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("Versionsvergleich", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsAlreadyCurrent()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha2",
            manifestVersion: "8.0.0-alpha2");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("ist aktuell", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsMinimumVersionRequirement()
    {
        using CheckFixture fixture = CreateCheckFixture(
            currentVersion: "8.0.0-alpha1",
            manifestVersion: "8.0.0-alpha3",
            minimumVersion: "8.0.0-alpha2");

        UpdateCheckResult result = await fixture.Service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("erfordert mindestens", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_ReportsTransportFailures()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler(
            _ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Contains("Updateprüfung fehlgeschlagen", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_PropagatesCancellation()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler(
            _ => throw new OperationCanceledException());
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CheckAsync());
    }

    [Fact]
    public async Task CheckAsync_UsesAssemblyVersionProvider_ByDefault()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler(JsonResponse("[]"));
        using var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(),
            directory.Path);

        UpdateCheckResult result = await service.CheckAsync();

        Assert.False(string.IsNullOrWhiteSpace(result.CurrentVersion));
        Assert.Contains("Kein GitHub-Release", result.Detail);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToProductChannel_WhenUpdateChannelBlank()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler(JsonResponse("[]"));
        using var client = new HttpClient(handler);
        var settings = new AppSettings();
        settings.Updates.Channel = " ";
        settings.Product.UpdateChannel = "Stable";
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(settings),
            new StubSignatureVerifier(),
            directory.Path,
            currentVersionProvider: () => "8.0.0");

        UpdateCheckResult result = await service.CheckAsync();

        Assert.Contains("Stable", result.Detail);
    }

    [Fact]
    public async Task DownloadAsync_WritesPackage_AndReportsProgress()
    {
        using var directory = new TemporaryDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("update-bytes");
        string sha = Convert.ToHexString(SHA256.HashData(payload));
        using var handler = new QueueHandler(
            _ => BinaryResponse(payload, setContentLength: true));
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            verifier: new StubSignatureVerifier(packageOk: true));
        var progress = new List<double>();
        var package = new UpdatePackage(
            "8.0.0-alpha2",
            "Alpha",
            new Uri("https://example.test/pkg.zip"),
            sha,
            payload.Length,
            "notes",
            Mandatory: false,
            CreateManifest("8.0.0-alpha2", sha, payload.Length));

        string path = await service.DownloadAsync(
            package,
            new Progress<double>(progress.Add));

        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.NotEmpty(progress);
        Assert.Contains(progress, value => value >= 1);
    }

    [Fact]
    public async Task DownloadAsync_UsesPackageSize_WhenContentLengthMissing()
    {
        using var directory = new TemporaryDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        string sha = Convert.ToHexString(SHA256.HashData(payload));
        using var handler = new QueueHandler(
            _ => BinaryResponse(payload, setContentLength: false));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);
        var package = new UpdatePackage(
            "8.0.0-alpha2",
            "Alpha",
            new Uri("https://example.test/pkg.zip"),
            sha,
            payload.Length,
            "notes",
            Mandatory: false);

        string path = await service.DownloadAsync(package);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task DownloadAsync_SkipsProgress_WhenTotalUnknown()
    {
        using var directory = new TemporaryDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        string sha = Convert.ToHexString(SHA256.HashData(payload));
        using var handler = new QueueHandler(
            _ => BinaryResponse(payload, setContentLength: false));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);
        var progress = new List<double>();
        var package = new UpdatePackage(
            "8.0.0-alpha2",
            "Alpha",
            new Uri("https://example.test/pkg.zip"),
            sha,
            0,
            "notes",
            Mandatory: false);

        await service.DownloadAsync(package, new Progress<double>(progress.Add));

        Assert.Empty(progress);
    }

    [Fact]
    public async Task DownloadAsync_RejectsInvalidSha()
    {
        using var directory = new TemporaryDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        using var handler = new QueueHandler(
            _ => BinaryResponse(payload, setContentLength: true));
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);
        var package = new UpdatePackage(
            "8.0.0-alpha2",
            "Alpha",
            new Uri("https://example.test/pkg.zip"),
            "DEADBEEF",
            payload.Length,
            "notes",
            Mandatory: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(package));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(directory.Path, "Downloads")));
    }

    [Fact]
    public async Task DownloadAsync_RejectsFailedPackageSignature()
    {
        using var directory = new TemporaryDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        string sha = Convert.ToHexString(SHA256.HashData(payload));
        using var handler = new QueueHandler(
            _ => BinaryResponse(payload, setContentLength: true));
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            verifier: new StubSignatureVerifier(packageOk: false));
        var package = new UpdatePackage(
            "8.0.0-alpha2",
            "Alpha",
            new Uri("https://example.test/pkg.zip"),
            sha,
            payload.Length,
            "notes",
            Mandatory: false,
            CreateManifest("8.0.0-alpha2", sha, payload.Length));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(package));
    }

    [Fact]
    public async Task DownloadAsync_ThrowsForNullPackage()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.DownloadAsync(null!));
    }

    [Fact]
    public async Task ApplyAsync_StartsUpdater_WithExpectedArguments()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        string updater = Path.Combine(install, "updater.exe");
        await File.WriteAllTextAsync(updater, "updater");
        ProcessStartInfo? started = null;
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(),
            directory.Path,
            currentVersionProvider: () => "1.0.0",
            installDirectory: install,
            mainExeName: "app.exe",
            updaterExeName: "updater.exe",
            processStarter: info =>
            {
                started = info;
                return new Process();
            });

        await service.ApplyAsync(package);

        Assert.NotNull(started);
        Assert.Equal(updater, started!.FileName);
        Assert.Equal(4, started.ArgumentList.Count);
        Assert.Equal(Path.GetFullPath(package), started.ArgumentList[0]);
        Assert.Equal(Path.GetFullPath(install), started.ArgumentList[1]);
        Assert.Equal("app.exe", started.ArgumentList[2]);
    }

    [Fact]
    public async Task ApplyAsync_Throws_WhenPackageMissing()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            installDirectory: directory.Path);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ApplyAsync(Path.Combine(directory.Path, "missing.zip")));
    }

    [Fact]
    public async Task ApplyAsync_Throws_WhenUpdaterMissing()
    {
        using var directory = new TemporaryDirectory();
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            installDirectory: directory.Path,
            updaterExeName: "missing-updater.exe");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ApplyAsync(package));
    }

    [Fact]
    public async Task ApplyAsync_Throws_WhenProcessStarterReturnsNull()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        await File.WriteAllTextAsync(
            Path.Combine(install, "updater.exe"),
            "updater");
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(),
            directory.Path,
            currentVersionProvider: () => "1.0.0",
            installDirectory: install,
            updaterExeName: "updater.exe",
            processStarter: _ => null);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyAsync(package));

        Assert.Contains("Updater konnte nicht gestartet", exception.Message);
    }

    [Fact]
    public async Task ApplyAsync_Throws_WhenWriteProbeDeniesAccess()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        await File.WriteAllTextAsync(
            Path.Combine(install, "updater.exe"),
            "updater");
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(),
            directory.Path,
            currentVersionProvider: () => "1.0.0",
            installDirectory: install,
            updaterExeName: "updater.exe",
            writeProbe: _ => throw new UnauthorizedAccessException());

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyAsync(package));

        Assert.Contains("Schreibrechte", exception.Message);
    }

    [Fact]
    public async Task ApplyAsync_Throws_WhenWriteProbeReportsAccessDeniedIo()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(install);
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        await File.WriteAllTextAsync(
            Path.Combine(install, "updater.exe"),
            "updater");
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(),
            directory.Path,
            currentVersionProvider: () => "1.0.0",
            installDirectory: install,
            updaterExeName: "updater.exe",
            writeProbe: _ => throw new IOException("Access is denied"));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyAsync(package));

        Assert.Contains("Schreibrechte", exception.Message);
    }

    [Fact]
    public async Task ApplyAsync_PropagatesCancellation()
    {
        using var directory = new TemporaryDirectory();
        string package = Path.Combine(directory.Path, "update.zip");
        await File.WriteAllTextAsync(package, "zip");
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(
            client,
            directory.Path,
            installDirectory: directory.Path,
            updaterExeName: "updater.exe");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ApplyAsync(
                package,
                new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task BackupAndRestore_RoundTripDataFiles()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "settings.json"),
            "{\"ok\":true}");
        foreach (string folder in new[] { "Profiles", "Overlay", "Secrets" })
        {
            string nested = Path.Combine(directory.Path, folder, "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, "file.txt"),
                folder);
        }

        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        UpdateBackup backup = await service.CreateBackupAsync("8.0.0-alpha1");
        IReadOnlyList<UpdateBackup> listed = await service.ListBackupsAsync();
        Assert.Contains(listed, item => item.Path == backup.Path);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "settings.json"),
            "changed");
        await service.RestoreBackupAsync(backup.Id);

        Assert.Equal(
            "{\"ok\":true}",
            await File.ReadAllTextAsync(
                Path.Combine(directory.Path, "settings.json")));
    }

    [Fact]
    public async Task CreateBackupAsync_SkipsMissingOptionalTrees()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        UpdateBackup backup = await service.CreateBackupAsync("1.0.0");

        Assert.True(File.Exists(backup.Path));
    }

    [Fact]
    public async Task RestoreBackupAsync_Throws_WhenBackupMissing()
    {
        using var directory = new TemporaryDirectory();
        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreBackupAsync("does-not-exist"));
    }

    [Fact]
    public async Task RestoreBackupAsync_CreatesDirectories_AndSkipsDirectoryEntries()
    {
        using var directory = new TemporaryDirectory();
        string backupRoot = Path.Combine(directory.Path, "Backups");
        Directory.CreateDirectory(backupRoot);
        string archivePath = Path.Combine(backupRoot, "backup-test-1.0.0.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Profiles/");
            ZipArchiveEntry file = archive.CreateEntry("Profiles/deep/item.txt");
            await using var writer = new StreamWriter(file.Open());
            await writer.WriteAsync("restored");
        }

        using var handler = new QueueHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, directory.Path);

        await service.RestoreBackupAsync("backup-test");

        Assert.Equal(
            "restored",
            await File.ReadAllTextAsync(
                Path.Combine(directory.Path, "Profiles", "deep", "item.txt")));
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("BETA")]
    [InlineData("nightly")]
    [InlineData("")]
    public void SelectRelease_NormalizesChannels(string channel)
    {
        var releases = new List<LocalUpdateService.GitHubRelease>
        {
            new()
            {
                TagName = "v8.0.0",
                Name = "Stable",
                Draft = true,
                Prerelease = false
            },
            new()
            {
                TagName = "v8.0.0",
                Name = "Stable",
                Prerelease = false
            },
            new()
            {
                TagName = "v8.0.0-beta1",
                Name = null,
                Prerelease = true
            },
            new()
            {
                TagName = null,
                Name = "mystery",
                Prerelease = true
            },
            new()
            {
                TagName = "v8.0.0-alpha1",
                Name = "Alpha",
                Prerelease = true
            }
        };

        LocalUpdateService.GitHubRelease? selected =
            LocalUpdateService.SelectRelease(releases, channel);

        Assert.NotNull(selected);
    }

    [Fact]
    public void SelectRelease_ReturnsNull_WhenOnlyDraftsExist()
    {
        var releases = new List<LocalUpdateService.GitHubRelease>
        {
            new()
            {
                TagName = "v1",
                Draft = true,
                Prerelease = false
            }
        };

        Assert.Null(LocalUpdateService.SelectRelease(releases, "Stable"));
    }

    [Fact]
    public void MatchesChannel_AcceptsPrereleaseWithoutAlphaAsBeta()
    {
        var release = new LocalUpdateService.GitHubRelease
        {
            TagName = "v8.0.0-rc1",
            Name = "Candidate",
            Prerelease = true
        };

        Assert.True(LocalUpdateService.MatchesChannel(release, "Beta"));
        Assert.False(
            LocalUpdateService.MatchesChannel(
                new LocalUpdateService.GitHubRelease
                {
                    TagName = "v8.0.0-alpha1",
                    Prerelease = true
                },
                "Beta"));
    }

    private static LocalUpdateService CreateService(
        HttpClient client,
        string dataRoot,
        string channel = "Alpha",
        string currentVersion = "8.0.0-alpha1",
        string? installDirectory = null,
        string updaterExeName = "CreatorControlSuite.Updater.exe",
        IUpdateSignatureVerifier? verifier = null)
    {
        var settings = new AppSettings();
        settings.Updates.Channel = channel;
        return new LocalUpdateService(
            client,
            new MemorySettingsStore(settings),
            verifier ?? new StubSignatureVerifier(),
            dataRoot,
            currentVersionProvider: () => currentVersion,
            installDirectory: installDirectory ?? dataRoot,
            updaterExeName: updaterExeName);
    }

    private static CheckFixture CreateCheckFixture(
        string currentVersion,
        string manifestVersion,
        string releaseNotes = "notes",
        string? releaseBody = null,
        string packageFileName = "pkg.zip",
        string? packageDownloadUrl = null,
        string productId = UpdateManifestCanonical.ProductId,
        string minimumVersion = "0.0.0",
        bool signatureValid = true,
        bool includePackageAsset = true)
    {
        var directory = new TemporaryDirectory();
        SignedUpdateManifest manifest = CreateManifest(
            manifestVersion,
            "ABC",
            10,
            productId,
            packageFileName,
            releaseNotes,
            minimumVersion);
        List<ReleaseAsset> assets =
        [
            new(
                "update-manifest.json",
                "https://example.test/update-manifest.json")
        ];
        if (includePackageAsset)
        {
            assets.Add(
                new(
                    packageFileName,
                    packageDownloadUrl ?? ("https://example.test/" + packageFileName)));
        }

        string releases = ReleasesJson(
            tag: "v" + manifestVersion,
            prerelease: true,
            body: releaseBody,
            assets: [.. assets]);

        var handler = new QueueHandler(
            JsonResponse(releases),
            JsonResponse(JsonSerializer.Serialize(manifest, JsonOptions)));
        var client = new HttpClient(handler);
        var service = new LocalUpdateService(
            client,
            new MemorySettingsStore(new AppSettings()),
            new StubSignatureVerifier(manifestOk: signatureValid),
            directory.Path,
            currentVersionProvider: () => currentVersion);

        return new CheckFixture(directory, handler, client, service);
    }

    private static SignedUpdateManifest CreateManifest(
        string version,
        string sha,
        long size,
        string productId = UpdateManifestCanonical.ProductId,
        string packageFileName = "pkg.zip",
        string releaseNotes = "notes",
        string minimumVersion = "0.0.0") =>
        new(
            productId,
            version,
            "Alpha",
            packageFileName,
            sha,
            size,
            DateTimeOffset.Parse("2026-07-29T00:00:00Z"),
            minimumVersion,
            releaseNotes,
            "sig");

    private static string ReleasesJson(
        string tag,
        bool prerelease,
        string? body = null,
        params ReleaseAsset[] assets)
    {
        var payload = new[]
        {
            new Dictionary<string, object?>
            {
                ["tag_name"] = tag,
                ["name"] = "Release",
                ["draft"] = false,
                ["prerelease"] = prerelease,
                ["body"] = body ?? string.Empty,
                ["assets"] = assets.Select(asset => new Dictionary<string, string>
                {
                    ["name"] = asset.Name,
                    ["browser_download_url"] = asset.Url
                }).ToArray()
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BinaryResponse(
        byte[] payload,
        bool setContentLength)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/octet-stream");
        if (setContentLength)
        {
            content.Headers.ContentLength = payload.Length;
        }
        else
        {
            content.Headers.ContentLength = null;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
    }

    private sealed record ReleaseAsset(string Name, string Url);

    private sealed class CheckFixture : IDisposable
    {
        public CheckFixture(
            TemporaryDirectory directory,
            QueueHandler handler,
            HttpClient client,
            LocalUpdateService service)
        {
            Directory = directory;
            Handler = handler;
            Client = client;
            Service = service;
        }

        public TemporaryDirectory Directory { get; }
        public QueueHandler Handler { get; }
        public HttpClient Client { get; }
        public LocalUpdateService Service { get; }

        public void Dispose()
        {
            Client.Dispose();
            Handler.Dispose();
            Directory.Dispose();
        }
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public QueueHandler()
        {
        }

        public QueueHandler(params HttpResponseMessage[] responses)
        {
            foreach (HttpResponseMessage response in responses)
            {
                HttpResponseMessage captured = response;
                _responses.Enqueue(_ => captured);
            }
        }

        public QueueHandler(
            params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            foreach (Func<HttpRequestMessage, HttpResponseMessage> response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Keine HTTP-Antwort konfiguriert.");
            }

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Uri);

    private sealed class StubSignatureVerifier(
        bool manifestOk = true,
        bool packageOk = true) : IUpdateSignatureVerifier
    {
        public bool VerifyManifest(SignedUpdateManifest manifest) => manifestOk;

        public Task<bool> VerifyPackageAsync(
            string packagePath,
            SignedUpdateManifest manifest,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(packageOk);
    }

    private sealed class MemorySettingsStore(AppSettings settings) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            AppSettings value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CreatorControlSuite.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
