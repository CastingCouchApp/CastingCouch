namespace CreatorControlSuite.Core.Diagnostics;

public interface ICrashReporter
{
    Task<string> WriteAsync(
        Exception exception,
        IReadOnlyDictionary<string, string>? context = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAsync(
        CancellationToken cancellationToken = default);
}
