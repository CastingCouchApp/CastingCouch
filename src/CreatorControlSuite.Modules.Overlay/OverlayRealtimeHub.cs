using System.Collections.Concurrent;
using System.Text.Json;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayRealtimeHub : IOverlayRealtimeHub
{
    public const string ChatMessageType = "channel.chat.message";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<Guid, Func<string, CancellationToken, Task>> _clients = new();
    private readonly object _chatGate = new();
    private readonly Queue<OverlayRealtimeEvent> _chatBuffer = new();
    private int _chatBufferCapacity = 100;

    public int ConnectedClients => _clients.Count;

    public void ConfigureChatBuffer(int maxMessages)
    {
        lock (_chatGate)
        {
            _chatBufferCapacity = Math.Clamp(maxMessages, 0, 1000);
            while (_chatBuffer.Count > _chatBufferCapacity)
            {
                _chatBuffer.Dequeue();
            }
        }
    }

    public IReadOnlyList<OverlayRealtimeEvent> GetBufferedChatEvents()
    {
        lock (_chatGate)
        {
            return _chatBuffer.ToArray();
        }
    }

    public string SerializeEvent(OverlayRealtimeEvent evt) =>
        JsonSerializer.Serialize(evt, JsonOptions);

    public void Register(Guid id, Func<string, CancellationToken, Task> sendAsync)
    {
        _clients[id] = sendAsync;
    }

    public void Unregister(Guid id)
    {
        _clients.TryRemove(id, out _);
    }

    public async Task PublishEventAsync(
        OverlayRealtimeEvent? evt,
        CancellationToken cancellationToken = default)
    {
        if (evt is null)
        {
            return;
        }

        if (IsChatMessage(evt))
        {
            BufferChat(evt);
        }

        string json = SerializeEvent(evt);
        if (_clients.IsEmpty)
        {
            return;
        }

        foreach (KeyValuePair<Guid, Func<string, CancellationToken, Task>> entry in _clients.ToArray())
        {
            try
            {
                await entry.Value(json, cancellationToken);
            }
            catch
            {
                _clients.TryRemove(entry.Key, out _);
            }
        }
    }

    private void BufferChat(OverlayRealtimeEvent evt)
    {
        lock (_chatGate)
        {
            if (_chatBufferCapacity <= 0)
            {
                _chatBuffer.Clear();
                return;
            }

            _chatBuffer.Enqueue(evt);
            while (_chatBuffer.Count > _chatBufferCapacity)
            {
                _chatBuffer.Dequeue();
            }
        }
    }

    private static bool IsChatMessage(OverlayRealtimeEvent evt) =>
        string.Equals(evt.Type, ChatMessageType, StringComparison.Ordinal) &&
        string.Equals(evt.Source, "twitch", StringComparison.OrdinalIgnoreCase);
}
