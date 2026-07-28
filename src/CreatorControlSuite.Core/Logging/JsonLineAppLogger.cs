using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Core.Logging;

public sealed class JsonLineAppLogger : IAppLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _logRoot;
    private readonly Lock _writeLock = new();
    private readonly Mutex _crossProcessWriteMutex;

    public JsonLineAppLogger(string logRoot)
    {
        _logRoot = logRoot;
        Directory.CreateDirectory(_logRoot);
        string normalizedRoot = Path.GetFullPath(_logRoot).ToUpperInvariant();
        string rootHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)));
        _crossProcessWriteMutex = new Mutex(
            initiallyOwned: false,
            name: @"Local\CreatorControlSuite.JsonLineAppLogger." + rootHash);
    }

    public event EventHandler<AppLogEntry>? EntryWritten;

    public void Write(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var entry = new AppLogEntry(
            DateTimeOffset.Now,
            level,
            category,
            SecretRedactor.Redact(message),
            SecretRedactor.Redact(exception?.ToString()),
            RedactProperties(properties, OperationCorrelation.CurrentId));

        string line = JsonSerializer.Serialize(entry, JsonOptions);
        string path = GetCurrentLogPath();

        lock (_writeLock)
        {
            bool mutexAcquired = false;
            try
            {
                try
                {
                    mutexAcquired = _crossProcessWriteMutex.WaitOne(
                        TimeSpan.FromSeconds(2));
                }
                catch (AbandonedMutexException)
                {
                    // The previous writer terminated while holding the mutex.
                    // Ownership is transferred to this process.
                    mutexAcquired = true;
                }

                if (mutexAcquired)
                {
                    WriteLineWithRetry(path, line);
                }
            }
            catch (IOException)
            {
                // Logging is best effort and must never terminate the application
                // when another process temporarily owns the active log file.
            }
            catch (UnauthorizedAccessException)
            {
                // The application must remain usable even if its log directory
                // temporarily becomes unavailable.
            }
            finally
            {
                if (mutexAcquired)
                {
                    _crossProcessWriteMutex.ReleaseMutex();
                }
            }
        }

        EntryWritten?.Invoke(this, entry);
    }

    private static IReadOnlyDictionary<string, string> RedactProperties(
        IReadOnlyDictionary<string, string>? properties,
        string? correlationId)
    {
        var result = properties?.ToDictionary(
            pair => pair.Key,
            pair => SecretRedactor.IsSensitiveKey(pair.Key)
                ? "[REDACTED]"
                : SecretRedactor.Redact(pair.Value),
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            result["correlationId"] = correlationId;
        }

        return result;
    }

    public async Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
        int maxEntries = 500,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<AppLogEntry>();
        var files = Directory.GetFiles(
                _logRoot,
                "suite-*.jsonl",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path)
            .Take(5)
            .ToList();

        foreach (string? file in files)
        {
            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(
                    file,
                    cancellationToken);
            }
            catch (IOException)
            {
                // The active log file can briefly be unavailable while another write completes.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string? line in lines.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    AppLogEntry? entry = JsonSerializer.Deserialize<AppLogEntry>(
                        line,
                        JsonOptions);

                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch
                {
                }

                if (entries.Count >= maxEntries)
                {
                    return entries;
                }
            }
        }

        return entries;
    }

    public async Task<string> ExportAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AppLogEntry> entries = await ReadRecentAsync(
            5000,
            cancellationToken);

        string[] lines = [.. entries
            .OrderBy(entry => entry.Timestamp)
            .Select(entry =>
                $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} " +
                $"[{entry.Level}] {entry.Category}: {entry.Message}" +
                (string.IsNullOrWhiteSpace(entry.Exception)
                    ? ""
                    : Environment.NewLine + entry.Exception))];

        await File.WriteAllLinesAsync(
            targetPath,
            lines,
            cancellationToken);

        return targetPath;
    }

    private string GetCurrentLogPath()
    {
        return Path.Combine(
            _logRoot,
            "suite-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl");
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);

        if (info.Length < 10 * 1024 * 1024)
        {
            return;
        }

        string rotated = Path.Combine(
            info.DirectoryName!,
            Path.GetFileNameWithoutExtension(path) +
            "-" +
            DateTime.Now.ToString("HHmmss") +
            ".jsonl");

        File.Move(path, rotated);
    }

    private static void WriteLineWithRetry(string path, string line)
    {
        const int attempts = 4;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                RotateIfNeeded(path);
                File.AppendAllText(
                    path,
                    line + Environment.NewLine,
                    Encoding.UTF8);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(25 * attempt);
            }
        }
    }
}
