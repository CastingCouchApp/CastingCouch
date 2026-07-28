using System.IO.Compression;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Tests;

public sealed class SupportPackageServiceTests
{
    [Fact]
    public async Task CreateAsync_UsesAllowlist_AndRedactsEveryTextSource()
    {
        using var directory = new TemporaryDirectory();
        string crashDirectory = Path.Combine(directory.Path, "CrashReports");
        string profileDirectory = Path.Combine(directory.Path, "Profiles");
        Directory.CreateDirectory(crashDirectory);
        Directory.CreateDirectory(profileDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(crashDirectory, "crash.json"),
            """{"password":"crash-secret"}""");
        await File.WriteAllBytesAsync(
            Path.Combine(crashDirectory, "memory.dmp"),
            [1, 2, 3]);
        await File.WriteAllTextAsync(
            Path.Combine(profileDirectory, "profile.json"),
            """{"agentKey":"device-secret"}""");
        var settings = new StaticSettingsStore(new AppSettings());
        var logger = new ExportLogger("Authorization: Bearer token-secret");
        var health = new RuntimeHealthService(settings, new SettingsValidator());
        var service = new SupportPackageService(
            directory.Path,
            settings,
            logger,
            health);
        string target = Path.Combine(directory.Path, "support.zip");

        SupportPackageResult result = await service.CreateAsync(
            target,
            new SupportPackageOptions(
                IncludeSettings: true,
                IncludeLogs: true,
                IncludeCrashReports: true,
                IncludeDiagnostics: false,
                IncludeProfiles: true,
                IncludeOverlayData: false));

        using ZipArchive archive = ZipFile.OpenRead(result.PackagePath);
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("memory.dmp", StringComparison.Ordinal));
        foreach (ZipArchiveEntry entry in archive.Entries.Where(item => item.Length > 0))
        {
            using var reader = new StreamReader(entry.Open());
            string content = await reader.ReadToEndAsync();
            Assert.DoesNotContain("crash-secret", content, StringComparison.Ordinal);
            Assert.DoesNotContain("device-secret", content, StringComparison.Ordinal);
            Assert.DoesNotContain("token-secret", content, StringComparison.Ordinal);
        }
    }

    private sealed class StaticSettingsStore(AppSettings value) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(value);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ExportLogger(string content) : IAppLogger
    {
        public event EventHandler<AppLogEntry>? EntryWritten;

        public void Write(
            AppLogLevel level,
            string category,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? properties = null)
        {
            _ = EntryWritten;
        }

        public Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
            int maxEntries = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppLogEntry>>([]);

        public async Task<string> ExportAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
            return targetPath;
        }
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

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
