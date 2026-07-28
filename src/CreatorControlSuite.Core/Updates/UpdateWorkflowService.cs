namespace CreatorControlSuite.Core.Updates;

public enum UpdateWorkflowPhase
{
    Downloading,
    CreatingBackup,
    Applying
}

public sealed record UpdateWorkflowOptions(
    bool BackupBeforeUpdate,
    string CurrentVersion);

public sealed record UpdateWorkflowProgress(
    UpdateWorkflowPhase Phase,
    double? DownloadProgress = null);

public sealed record UpdateWorkflowResult(
    IReadOnlyList<UpdateBackup> Backups);

public sealed class UpdateWorkflowService(IUpdateService updates)
{
    public Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default) =>
        updates.CheckAsync(cancellationToken);

    public async Task<UpdateWorkflowResult> InstallAsync(
        UpdatePackage package,
        UpdateWorkflowOptions options,
        IProgress<UpdateWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        progress?.Report(new UpdateWorkflowProgress(
            UpdateWorkflowPhase.Downloading,
            0));
        var downloadProgress = new Progress<double>(value =>
            progress?.Report(new UpdateWorkflowProgress(
                UpdateWorkflowPhase.Downloading,
                value)));
        string packagePath = await updates.DownloadAsync(
            package,
            downloadProgress,
            cancellationToken);

        IReadOnlyList<UpdateBackup> backups = [];
        if (options.BackupBeforeUpdate)
        {
            progress?.Report(new UpdateWorkflowProgress(
                UpdateWorkflowPhase.CreatingBackup));
            await updates.CreateBackupAsync(
                options.CurrentVersion,
                cancellationToken);
            backups = await updates.ListBackupsAsync(cancellationToken);
        }

        progress?.Report(new UpdateWorkflowProgress(
            UpdateWorkflowPhase.Applying));
        await updates.ApplyAsync(packagePath, cancellationToken);
        return new UpdateWorkflowResult(backups);
    }
}
