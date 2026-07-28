namespace CreatorControlSuite.Core.Logging;

public static class OperationCorrelation
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string? CurrentId => CurrentValue.Value;

    public static IDisposable Begin(string? correlationId = null)
    {
        string? previous = CurrentValue.Value;
        CurrentValue.Value = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentValue.Value = previous;
            _disposed = true;
        }
    }
}
