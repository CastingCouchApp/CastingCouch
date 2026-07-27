using System.Runtime.InteropServices;
using System.Text.Json;

namespace CreatorControlSuite.Core.Diagnostics;

public sealed class FileCrashReporter : ICrashReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _crashRoot;

    public FileCrashReporter(string crashRoot)
    {
        _crashRoot = crashRoot;
        Directory.CreateDirectory(_crashRoot);
    }

    public async Task<string> WriteAsync(
        Exception exception,
        IReadOnlyDictionary<string, string>? context = null,
        CancellationToken cancellationToken = default)
    {
        string id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        var report = new CrashReport(
            id,
            DateTimeOffset.Now,
            "2.0.81",
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace ?? "",
            exception.ToString(),
            context ??
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase));

        string path = Path.Combine(
            _crashRoot,
            "crash-" + id + ".json");

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);

        return path;
    }

    public Task<IReadOnlyList<string>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> reports =
            [.. Directory.GetFiles(
                    _crashRoot,
                    "crash-*.json",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path)];

        return Task.FromResult(reports);
    }
}
