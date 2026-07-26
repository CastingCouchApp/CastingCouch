namespace CreatorControlSuite.Core.Updates;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        string packageZipPath,
        CancellationToken cancellationToken = default);

    Task<UpdateBackup> CreateBackupAsync(
        string currentVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UpdateBackup>> ListBackupsAsync(
        CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default);
}
