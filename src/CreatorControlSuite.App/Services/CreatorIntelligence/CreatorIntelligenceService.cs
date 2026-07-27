using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CreatorControlSuite.App.Services.CreatorIntelligence;

public sealed partial class CreatorIntelligenceService : IAsyncDisposable
{

    private readonly Channel<CreatorIntelligenceEvent> _queue = Channel.CreateBounded<CreatorIntelligenceEvent>(
        new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerTask;
    private readonly ConcurrentDictionary<string, CreatorIntelligenceEvent> _latestByType = new(StringComparer.OrdinalIgnoreCase);

    public CreatorIntelligenceService()
    {
        RootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "CreatorIntelligence");
        Directory.CreateDirectory(RootDirectory);
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public bool IsRecording => !string.IsNullOrWhiteSpace(SessionId);
    public string? SessionId { get; private set; }
    public string RootDirectory { get; }

    public async Task StartSessionAsync(DateTimeOffset startedAt, string? title, string? category, CancellationToken cancellationToken = default)
    {
        if (IsRecording)
        {
            return;
        }

        SessionId = $"{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        await RecordAsync("session.started", new { startedAt, title, category }, cancellationToken);
    }

    public ValueTask RecordAsync(string type, object? payload = null, CancellationToken cancellationToken = default)
    {
        if (!IsRecording && !string.Equals(type, "session.started", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.CompletedTask;
        }

        var item = new CreatorIntelligenceEvent(
            DateTimeOffset.UtcNow,
            SessionId ?? "unassigned",
            type,
            payload is null ? null : JsonSerializer.SerializeToElement(payload));
        _latestByType[type] = item;
        return _queue.Writer.WriteAsync(item, cancellationToken);
    }

    public async Task<CreatorIntelligenceSummary?> CompleteSessionAsync(DateTimeOffset endedAt, CancellationToken cancellationToken = default)
    {
        if (!IsRecording)
        {
            return null;
        }

        string sessionId = SessionId!;
        await RecordAsync("session.ended", new { endedAt }, cancellationToken);
        SessionId = null;
        await FlushAsync(cancellationToken);
        return await AnalyzeSessionAsync(sessionId, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        string marker = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FlushWaiters[marker] = completion;
        await _queue.Writer.WriteAsync(new CreatorIntelligenceEvent(DateTimeOffset.UtcNow, "system", "system.flush", JsonSerializer.SerializeToElement(new { marker })), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    private ConcurrentDictionary<string, TaskCompletionSource> FlushWaiters { get; } = new();

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (CreatorIntelligenceEvent item in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                if (item.Type == "system.flush" && item.Payload is { } flushPayload && flushPayload.TryGetProperty("marker", out JsonElement markerElement))
                {
                    string? marker = markerElement.GetString();
                    if (marker is not null && FlushWaiters.TryRemove(marker, out TaskCompletionSource? waiter))
                    {
                        waiter.TrySetResult();
                    }

                    continue;
                }

                string dayFolder = Path.Combine(RootDirectory, item.TimestampUtc.ToLocalTime().ToString("yyyy-MM"));
                Directory.CreateDirectory(dayFolder);
                string path = Path.Combine(dayFolder, "events.jsonl");
                string line = JsonSerializer.Serialize(item);
                await File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false), _shutdown.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _shutdown.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _writerTask; } catch { }
        _shutdown.Dispose();
    }
}
