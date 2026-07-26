namespace CreatorControlSuite.Core.Logging;

public interface IAppLogger
{
    event EventHandler<AppLogEntry>? EntryWritten;

    void Write(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null);

    Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
        int maxEntries = 500,
        CancellationToken cancellationToken = default);

    Task<string> ExportAsync(
        string targetPath,
        CancellationToken cancellationToken = default);
}
