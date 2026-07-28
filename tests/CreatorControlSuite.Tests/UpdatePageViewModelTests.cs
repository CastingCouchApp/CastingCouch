using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class UpdatePageViewModelTests
{
    [Fact]
    public async Task LoadAndApply_RoundTripsUpdateSettings()
    {
        var updates = new FakeUpdateService();
        var viewModel = CreateViewModel(updates);
        var settings = new UpdateSettings
        {
            AutoCheck = false,
            BackupBeforeUpdate = true,
            Channel = "Beta"
        };

        await viewModel.LoadAsync(settings);
        viewModel.AutoCheck = true;
        viewModel.BackupBeforeUpdate = false;
        viewModel.UpdateChannel = "Stable";
        viewModel.ApplyTo(settings);

        Assert.True(settings.AutoCheck);
        Assert.False(settings.BackupBeforeUpdate);
        Assert.Equal("Stable", settings.Channel);
    }

    [Fact]
    public async Task CheckAsync_EnablesInstallForAvailablePackage()
    {
        var updates = new FakeUpdateService
        {
            CheckResult = new UpdateCheckResult(
                true,
                "8.0.0-alpha1",
                Package(),
                "verfügbar")
        };
        var viewModel = CreateViewModel(updates);

        await viewModel.CheckAsync();

        Assert.True(viewModel.CanInstall);
        Assert.True(viewModel.StatusIsSuccess);
        Assert.Contains("8.0.0-alpha2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RestoreSelectedBackup_RequiresConfirmationAndReloads()
    {
        var backup = new UpdateBackup(
            "backup",
            "8.0.0-alpha1",
            "/tmp/backup",
            DateTimeOffset.Now,
            42);
        var updates = new FakeUpdateService { Backups = [backup] };
        var viewModel = CreateViewModel(updates);
        bool reloaded = false;
        viewModel.ConfirmRestoreAsync = () => Task.FromResult(true);
        viewModel.AfterRestoreAsync = () =>
        {
            reloaded = true;
            return Task.CompletedTask;
        };
        await viewModel.LoadAsync(new UpdateSettings());
        viewModel.SelectedBackup = backup;

        await viewModel.RestoreSelectedBackupAsync();

        Assert.Equal("backup", updates.RestoredBackupId);
        Assert.True(reloaded);
        Assert.True(viewModel.StatusIsSuccess);
    }

    private static UpdatePageViewModel CreateViewModel(
        FakeUpdateService updates) =>
        new(
            new UpdateWorkflowService(updates),
            updates,
            () => "8.0.0-alpha1");

    private static UpdatePackage Package() => new(
        "8.0.0-alpha2",
        "Alpha",
        new Uri("https://example.invalid/update.zip"),
        "hash",
        42,
        "notes",
        Mandatory: false);

    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateCheckResult CheckResult { get; set; } = new(
            false,
            "8.0.0-alpha1",
            null,
            "aktuell");

        public IReadOnlyList<UpdateBackup> Backups { get; set; } = [];
        public string? RestoredBackupId { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CheckResult);

        public Task<string> DownloadAsync(
            UpdatePackage package,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("/tmp/update.zip");

        public Task ApplyAsync(
            string packageZipPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<UpdateBackup> CreateBackupAsync(
            string currentVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateBackup(
                "new",
                currentVersion,
                "/tmp/new",
                DateTimeOffset.Now,
                42));

        public Task<IReadOnlyList<UpdateBackup>> ListBackupsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Backups);

        public Task RestoreBackupAsync(
            string backupId,
            CancellationToken cancellationToken = default)
        {
            RestoredBackupId = backupId;
            return Task.CompletedTask;
        }
    }
}
