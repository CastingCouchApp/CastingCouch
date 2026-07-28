using System.Diagnostics;
using CreatorControlSuite.Core.Updates;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Updater <package.zip> <installDir> <mainExe> [waitPid]");
    return 2;
}

string package = Path.GetFullPath(args[0]);
string install = Path.GetFullPath(args[1]);
string mainExe = args[2];
if (!File.Exists(package))
{
    return 3;
}

if (args.Length > 3 && int.TryParse(args[3], out int processId))
{
    try
    {
        using Process process = Process.GetProcessById(processId);
        await process.WaitForExitAsync();
    }
    catch
    {
    }
}

string transactionId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
string transactionRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CreatorControlSuite",
    "UpdateTransactions");
Directory.CreateDirectory(transactionRoot);
foreach (string incomplete in Directory.GetDirectories(
             transactionRoot,
             "CreatorControlSuite.Update.*",
             SearchOption.TopDirectoryOnly))
{
    UpdateTransactionJournal? recovered =
        await FileUpdateTransaction.RecoverAsync(incomplete);
    if (recovered?.State == "RollbackFailed")
    {
        Console.Error.WriteLine(
            $"Vorherige Update-Transaktion konnte nicht wiederhergestellt werden: {incomplete}");
        return 4;
    }
}

string transactionDirectory = Path.Combine(
    transactionRoot,
    "CreatorControlSuite.Update." + transactionId);
try
{
    await new FileUpdateTransaction().ApplyAsync(
        package,
        install,
        transactionDirectory);
    string executable = Path.Combine(install, mainExe);
    if (File.Exists(executable))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = install,
            UseShellExecute = true
        });
    }

    try { Directory.Delete(transactionDirectory, recursive: true); }
    catch { }
    return 0;
}
catch (UpdateTransactionException exception)
{
    Console.Error.WriteLine(exception);
    return exception.Journal.State == "RolledBack" ? 1 : 4;
}
