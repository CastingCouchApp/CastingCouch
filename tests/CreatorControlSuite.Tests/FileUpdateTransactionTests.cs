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
