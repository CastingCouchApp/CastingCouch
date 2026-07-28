using System.IO.Compression;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class FileUpdateTransactionTests
{
    [Fact]
    public async Task ApplyAsync_RollsBackChangedAndNewFiles_AfterPartialFailure()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string packageSource = Path.Combine(directory.Path, "package");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(packageSource);
        await File.WriteAllTextAsync(
            Path.Combine(install, "existing.txt"),
            "old");
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "existing.txt"),
            "new");
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "new.txt"),
            "created");
        string package = Path.Combine(directory.Path, "update.zip");
        ZipFile.CreateFromDirectory(packageSource, package);
        var transactionRunner = new FileUpdateTransaction(
            (relative, _) => relative == "new.txt"
                ? Task.FromException(new IOException("simulated lock"))
                : Task.CompletedTask);

        UpdateTransactionException exception =
            await Assert.ThrowsAsync<UpdateTransactionException>(() =>
                transactionRunner.ApplyAsync(package, install, transaction));

        Assert.Equal("RolledBack", exception.Journal.State);
        Assert.Empty(exception.Journal.RollbackErrors);
        Assert.Equal(
            "old",
            await File.ReadAllTextAsync(Path.Combine(install, "existing.txt")));
        Assert.False(File.Exists(Path.Combine(install, "new.txt")));
        Assert.True(File.Exists(Path.Combine(transaction, "transaction.json")));
    }

    [Fact]
    public async Task ApplyAsync_LeavesCompletedInstallAndJournal()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string packageSource = Path.Combine(directory.Path, "package");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(packageSource);
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "app.txt"),
            "new");
        string package = Path.Combine(directory.Path, "update.zip");
        ZipFile.CreateFromDirectory(packageSource, package);

        UpdateTransactionJournal journal =
            await new FileUpdateTransaction().ApplyAsync(
                package,
                install,
                transaction);

        Assert.Equal("Completed", journal.State);
        Assert.Equal(
            "new",
            await File.ReadAllTextAsync(Path.Combine(install, "app.txt")));
    }

    [Fact]
    public async Task RecoverAsync_RollsBackJournalLeftByProcessInterruption()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string transaction = Path.Combine(directory.Path, "transaction");
        string backup = Path.Combine(transaction, "backup");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(backup);
        await File.WriteAllTextAsync(Path.Combine(install, "existing.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(backup, "existing.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(install, "new.txt"), "created");
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "interrupted",
            InstallDirectory = install,
            State = "Applying",
            AppliedFiles = ["existing.txt", "new.txt"]
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal("RolledBack", recovered.State);
        Assert.Equal(
            "old",
            await File.ReadAllTextAsync(Path.Combine(install, "existing.txt")));
        Assert.False(File.Exists(Path.Combine(install, "new.txt")));
    }

    [Fact]
    public async Task RecoverAsync_RollsBackWriteAheadPendingFile_AfterInterruption()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string transaction = Path.Combine(directory.Path, "transaction");
        string backup = Path.Combine(transaction, "backup");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(backup);
        await File.WriteAllTextAsync(
            Path.Combine(install, "existing.txt"),
            "partially-applied");
        await File.WriteAllTextAsync(
            Path.Combine(backup, "existing.txt"),
            "old");
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "interrupted-before-commit",
            InstallDirectory = install,
            State = "Applying",
            PendingFile = "existing.txt"
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal("RolledBack", recovered.State);
        Assert.Null(recovered.PendingFile);
        Assert.Equal(
            "old",
            await File.ReadAllTextAsync(Path.Combine(install, "existing.txt")));
    }

    [Fact]
    public async Task RecoverAsync_RemovesWriteAheadPendingNewFile_AfterInterruption()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(transaction);
        await File.WriteAllTextAsync(
            Path.Combine(install, "new.txt"),
            "partially-applied");
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "interrupted-new-file",
            InstallDirectory = install,
            State = "Applying",
            PendingFile = "new.txt"
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal("RolledBack", recovered.State);
        Assert.Null(recovered.PendingFile);
        Assert.False(File.Exists(Path.Combine(install, "new.txt")));
    }

    [Fact]
    public async Task RecoverAsync_ClearsPreviousErrors_WhenRollbackRetrySucceeds()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string transaction = Path.Combine(directory.Path, "transaction");
        string backup = Path.Combine(transaction, "backup");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(backup);
        await File.WriteAllTextAsync(
            Path.Combine(install, "existing.txt"),
            "new");
        await File.WriteAllTextAsync(
            Path.Combine(backup, "existing.txt"),
            "old");
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "rollback-retry",
            InstallDirectory = install,
            State = "RollbackFailed",
            Error = "first rollback attempt failed",
            PendingFile = "existing.txt",
            RollbackErrors = ["existing.txt: simulated lock"]
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal("RolledBack", recovered.State);
        Assert.Empty(recovered.RollbackErrors);
        Assert.Null(recovered.PendingFile);
        Assert.Equal("first rollback attempt failed", recovered.Error);
        Assert.Equal(
            "old",
            await File.ReadAllTextAsync(Path.Combine(install, "existing.txt")));
    }

    [Fact]
    public async Task ApplyAsync_RollsBack_WhenCancellationInterruptsMultipleFiles()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string packageSource = Path.Combine(directory.Path, "package");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(packageSource);
        await File.WriteAllTextAsync(Path.Combine(install, "a.txt"), "old-a");
        await File.WriteAllTextAsync(Path.Combine(install, "b.txt"), "old-b");
        await File.WriteAllTextAsync(Path.Combine(packageSource, "a.txt"), "new-a");
        await File.WriteAllTextAsync(Path.Combine(packageSource, "b.txt"), "new-b");
        string package = Path.Combine(directory.Path, "update.zip");
        ZipFile.CreateFromDirectory(packageSource, package);
        using var cancellation = new CancellationTokenSource();
        var transactionRunner = new FileUpdateTransaction(
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        UpdateTransactionException exception =
            await Assert.ThrowsAsync<UpdateTransactionException>(() =>
                transactionRunner.ApplyAsync(
                    package,
                    install,
                    transaction,
                    cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(
            exception.InnerException);
        Assert.Equal("RolledBack", exception.Journal.State);
        Assert.Equal(
            "old-a",
            await File.ReadAllTextAsync(Path.Combine(install, "a.txt")));
        Assert.Equal(
            "old-b",
            await File.ReadAllTextAsync(Path.Combine(install, "b.txt")));
    }

    [Fact]
    public async Task ApplyAsync_ReportsRollbackFailed_WhenBackupCannotReplaceDirectory()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string packageSource = Path.Combine(directory.Path, "package");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(packageSource);
        string destination = Path.Combine(install, "existing.txt");
        await File.WriteAllTextAsync(destination, "old");
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "existing.txt"),
            "new");
        string package = Path.Combine(directory.Path, "update.zip");
        ZipFile.CreateFromDirectory(packageSource, package);
        var transactionRunner = new FileUpdateTransaction(
            (_, _) =>
            {
                File.Delete(destination);
                Directory.CreateDirectory(destination);
                return Task.FromException(
                    new IOException("simulated failure after apply"));
            });

        UpdateTransactionException exception =
            await Assert.ThrowsAsync<UpdateTransactionException>(() =>
                transactionRunner.ApplyAsync(package, install, transaction));

        Assert.Equal("RollbackFailed", exception.Journal.State);
        Assert.NotEmpty(exception.Journal.RollbackErrors);
    }

    [Fact]
    public async Task RecoverAsync_ReturnsNull_WhenJournalDoesNotExist()
    {
        using var directory = new TemporaryDirectory();

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(
                Path.Combine(directory.Path, "missing-transaction"));

        Assert.Null(recovered);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("RolledBack")]
    public async Task RecoverAsync_LeavesTerminalJournalUnchanged(string state)
    {
        using var directory = new TemporaryDirectory();
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(transaction);
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "terminal",
            InstallDirectory = Path.Combine(directory.Path, "install"),
            State = state
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal(state, recovered.State);
    }

    [Fact]
    public async Task RecoverAsync_ReturnsNull_WhenJournalContainsJsonNull()
    {
        using var directory = new TemporaryDirectory();
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(transaction);
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            "null");

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.Null(recovered);
    }

    [Fact]
    public async Task RecoverAsync_ReportsRollbackFailed_ForUnsafeJournalPath()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        string transaction = Path.Combine(directory.Path, "transaction");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(transaction);
        var journal = new UpdateTransactionJournal
        {
            TransactionId = "unsafe-journal",
            InstallDirectory = install,
            State = "Applying",
            PendingFile = "../outside.txt"
        };
        await File.WriteAllTextAsync(
            Path.Combine(transaction, "transaction.json"),
            System.Text.Json.JsonSerializer.Serialize(journal));

        UpdateTransactionJournal? recovered =
            await FileUpdateTransaction.RecoverAsync(transaction);

        Assert.NotNull(recovered);
        Assert.Equal("RollbackFailed", recovered.State);
        Assert.NotEmpty(recovered.RollbackErrors);
        Assert.Equal("../outside.txt", recovered.PendingFile);
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
