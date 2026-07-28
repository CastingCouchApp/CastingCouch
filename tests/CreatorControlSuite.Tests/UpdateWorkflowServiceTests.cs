using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class UpdateWorkflowServiceTests
{
    [Fact]
    public async Task InstallAsync_DownloadsBacksUpAndAppliesInOrder()
    {
        var updateService = new RecordingUpdateService();
        var workflow = new UpdateWorkflowService(updateService);
        var progress = new List<UpdateWorkflowProgress>();

        UpdateWorkflowResult result = await workflow.InstallAsync(
            Package(),
            new UpdateWorkflowOptions(
                BackupBeforeUpdate: true,
                CurrentVersion: "8.0.0-alpha1"),
            new ImmediateProgress<UpdateWorkflowProgress>(progress.Add));

        Assert.Equal(
            ["download", "backup", "list-backups", "apply"],
            updateService.Calls);
        Assert.Single(result.Backups);
        Assert.Contains(
            progress,
            item => item.Phase == UpdateWorkflowPhase.Downloading);
        Assert.Contains(
            progress,
            item => item.Phase == UpdateWorkflowPhase.Applying);
    }

    [Fact]
    public async Task InstallAsync_SkipsBackupWhenDisabled()
    {
        var updateService = new RecordingUpdateService();
        var workflow = new UpdateWorkflowService(updateService);

        UpdateWorkflowResult result = await workflow.InstallAsync(
            Package(),
            new UpdateWorkflowOptions(
                BackupBeforeUpdate: false,
                CurrentVersion: "8.0.0-alpha1"));

        Assert.Equal(["download", "apply"], updateService.Calls);
        Assert.Empty(result.Backups);
    }

    private static UpdatePackage Package() => new(
        "8.0.0-alpha2",
        "Alpha",
        new Uri("https://example.invalid/update.zip"),
        "hash",
        42,
        "notes",
        Mandatory: false);

    private sealed class RecordingUpdateService : IUpdateService
    {
        public List<string> Calls { get; } = [];

        public Task<UpdateCheckResult> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(
                false,
                "8.0.0-alpha1",
                null,
                "aktuell"));

        public Task<string> DownloadAsync(
            UpdatePackage package,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("download");
            progress?.Report(0.5);
            return Task.FromResult("/tmp/update.zip");
        }

        public Task ApplyAsync(
            string packageZipPath,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("apply");
            return Task.CompletedTask;
        }

        public Task<UpdateBackup> CreateBackupAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("backup");
            return Task.FromResult(
                new UpdateBackup(
                    "backup",
                    currentVersion,
                    "/tmp/backup",
                    DateTimeOffset.Now,
                    42));
        }

        public Task<IReadOnlyList<UpdateBackup>> ListBackupsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls.Add("list-backups");
            return Task.FromResult<IReadOnlyList<UpdateBackup>>(
                [new UpdateBackup(
                    "backup",
                    "8.0.0-alpha1",
                    "/tmp/backup",
                    DateTimeOffset.Now,
                    42)]);
        }

        public Task RestoreBackupAsync(
            string backupId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateProgress<T>(
        Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
