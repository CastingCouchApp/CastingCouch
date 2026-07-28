using System.Text.Json;

namespace CreatorControlSuite.Core.Updates;

public sealed class FileUpdateTransaction(
    Func<string, CancellationToken, Task>? afterFileApplied = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<UpdateTransactionJournal> ApplyAsync(
        string packagePath,
        string installDirectory,
        string transactionDirectory,
        CancellationToken cancellationToken = default)
    {
        string package = Path.GetFullPath(packagePath);
        string install = Path.GetFullPath(installDirectory);
        string transaction = Path.GetFullPath(transactionDirectory);
        string staging = Path.Combine(transaction, "staging");
        string backup = Path.Combine(transaction, "backup");
        string journalPath = Path.Combine(transaction, "transaction.json");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backup);
        var journal = new UpdateTransactionJournal
        {
            TransactionId = Path.GetFileName(transaction),
            PackagePath = package,
            InstallDirectory = install,
            StartedAt = DateTimeOffset.UtcNow,
            State = "Preparing"
        };
        await SaveAsync(journalPath, journal, cancellationToken);

        try
        {
            SafeZipExtractor.ExtractToDirectory(
                package,
                staging,
                overwriteFiles: true);
            string[] files = [.. Directory
                .GetFiles(staging, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
            journal.State = "BackingUp";
            await SaveAsync(journalPath, journal, cancellationToken);
            foreach (string source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(staging, source);
                string destination = SafeZipExtractor.ResolveDestinationPath(
                    install,
                    relative);
                if (!File.Exists(destination))
                {
                    continue;
                }

                string backupPath = SafeZipExtractor.ResolveDestinationPath(
                    backup,
                    relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, overwrite: true);
                journal.BackedUpFiles.Add(relative);
            }

            journal.State = "Applying";
            await SaveAsync(journalPath, journal, cancellationToken);
            foreach (string source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(staging, source);
                string destination = SafeZipExtractor.ResolveDestinationPath(
                    install,
                    relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                journal.AppliedFiles.Add(relative);
                await SaveAsync(journalPath, journal, cancellationToken);
                if (afterFileApplied is not null)
                {
                    await afterFileApplied(relative, cancellationToken);
                }
            }

            journal.State = "Completed";
            journal.CompletedAt = DateTimeOffset.UtcNow;
            await SaveAsync(journalPath, journal, cancellationToken);
            return journal;
        }
        catch (Exception exception)
        {
            journal.State = "RollingBack";
            journal.Error = exception.ToString();
            await SaveWithoutCancellationAsync(journalPath, journal);
            foreach (string relative in journal.AppliedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    string destination = SafeZipExtractor.ResolveDestinationPath(
                        install,
                        relative);
                    string backupPath = SafeZipExtractor.ResolveDestinationPath(
                        backup,
                        relative);
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, destination, overwrite: true);
                    }
                    else if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }
                catch (Exception rollbackException)
                {
                    journal.RollbackErrors.Add(
                        relative + ": " + rollbackException.Message);
                }
            }

            journal.State = journal.RollbackErrors.Count == 0
                ? "RolledBack"
                : "RollbackFailed";
            journal.CompletedAt = DateTimeOffset.UtcNow;
            await SaveWithoutCancellationAsync(journalPath, journal);
            throw new UpdateTransactionException(
                "Update-Transaktion ist fehlgeschlagen.",
                journal,
                exception);
        }
    }

    public static async Task<UpdateTransactionJournal?> RecoverAsync(
        string transactionDirectory,
        CancellationToken cancellationToken = default)
    {
        string transaction = Path.GetFullPath(transactionDirectory);
        string journalPath = Path.Combine(transaction, "transaction.json");
        if (!File.Exists(journalPath))
        {
            return null;
        }

        UpdateTransactionJournal? journal =
            JsonSerializer.Deserialize<UpdateTransactionJournal>(
                await File.ReadAllTextAsync(journalPath, cancellationToken));
        if (journal is null ||
            journal.State is "Completed" or "RolledBack")
        {
            return journal;
        }

        string install = Path.GetFullPath(journal.InstallDirectory);
        string backup = Path.Combine(transaction, "backup");
        journal.State = "RollingBack";
        journal.Error = string.IsNullOrWhiteSpace(journal.Error)
            ? "Unvollständige Update-Transaktion beim Start erkannt."
            : journal.Error;
        foreach (string relative in journal.AppliedFiles.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string destination = SafeZipExtractor.ResolveDestinationPath(
                    install,
                    relative);
                string backupPath = SafeZipExtractor.ResolveDestinationPath(
                    backup,
                    relative);
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, overwrite: true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception exception)
            {
                journal.RollbackErrors.Add(relative + ": " + exception.Message);
            }
        }

        journal.State = journal.RollbackErrors.Count == 0
            ? "RolledBack"
            : "RollbackFailed";
        journal.CompletedAt = DateTimeOffset.UtcNow;
        await SaveAsync(journalPath, journal, cancellationToken);
        return journal;
    }

    private static async Task SaveAsync(
        string path,
        UpdateTransactionJournal journal,
        CancellationToken cancellationToken)
    {
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(journal, JsonOptions),
            cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private static Task SaveWithoutCancellationAsync(
        string path,
        UpdateTransactionJournal journal) =>
        SaveAsync(path, journal, CancellationToken.None);
}

public sealed class UpdateTransactionException(
    string message,
    UpdateTransactionJournal journal,
    Exception innerException) : Exception(message, innerException)
{
    public UpdateTransactionJournal Journal { get; } = journal;
}

public sealed class UpdateTransactionJournal
{
    public string TransactionId { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string InstallDirectory { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string State { get; set; } = "";
    public string Error { get; set; } = "";
    public List<string> BackedUpFiles { get; set; } = [];
    public List<string> AppliedFiles { get; set; } = [];
    public List<string> RollbackErrors { get; set; } = [];
}
