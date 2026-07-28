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

    public event Action? ChatBufferChanged;

    public void ConfigureChatBuffer(int maxMessages)
    {
        lock (_chatGate)
        {
            _chatBufferCapacity = Math.Clamp(maxMessages, 0, 2000);
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

    public void RestoreBufferedChatEvents(IEnumerable<OverlayRealtimeEvent> events)
    {
        lock (_chatGate)
        {
            _chatBuffer.Clear();
            if (_chatBufferCapacity <= 0)
            {
                return;
            }

            foreach (OverlayRealtimeEvent evt in events)
            {
                if (!IsChatMessage(evt))
                {
                    continue;
                }

                _chatBuffer.Enqueue(evt);
            }

            while (_chatBuffer.Count > _chatBufferCapacity)
            {
                _chatBuffer.Dequeue();
            }
        }
    }

    public void ClearBufferedChat()
    {
        lock (_chatGate)
        {
            _chatBuffer.Clear();
        }

        ChatBufferChanged?.Invoke();
    }

    public bool RemoveBufferedChatMessage(string messageId)
    {
        string id = (messageId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        bool removed;
        lock (_chatGate)
        {
            int before = _chatBuffer.Count;
            OverlayRealtimeEvent[] kept =
            [
                .. _chatBuffer.Where(evt =>
                    !string.Equals(
                        GetData(evt, "messageId"),
                        id,
                        StringComparison.OrdinalIgnoreCase))
            ];
            _chatBuffer.Clear();
            foreach (OverlayRealtimeEvent evt in kept)
            {
                _chatBuffer.Enqueue(evt);
            }

            removed = kept.Length != before;
        }

        if (removed)
        {
            ChatBufferChanged?.Invoke();
        }

        return removed;
    }

    public int RemoveBufferedChatMessagesByUser(string userLogin, string userId)
    {
        string login = (userLogin ?? "").Trim();
        string id = (userId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(login) && string.IsNullOrWhiteSpace(id))
        {
            return 0;
        }

        int removed;
        lock (_chatGate)
        {
            int before = _chatBuffer.Count;
            OverlayRealtimeEvent[] kept =
            [
                .. _chatBuffer.Where(evt =>
                {
                    string evtLogin = GetData(evt, "userLogin");
                    string evtId = GetData(evt, "userId");
                    bool matchLogin = !string.IsNullOrWhiteSpace(login) &&
                        string.Equals(evtLogin, login, StringComparison.OrdinalIgnoreCase);
                    bool matchId = !string.IsNullOrWhiteSpace(id) &&
                        string.Equals(evtId, id, StringComparison.Ordinal);
                    return !matchLogin && !matchId;
                })
            ];
            _chatBuffer.Clear();
            foreach (OverlayRealtimeEvent evt in kept)
            {
                _chatBuffer.Enqueue(evt);
            }

            removed = before - kept.Length;
        }

        if (removed > 0)
        {
            ChatBufferChanged?.Invoke();
        }

        return removed;
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
            ChatBufferChanged?.Invoke();
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

    private static string GetData(OverlayRealtimeEvent evt, string key) =>
        evt.Data.TryGetValue(key, out string? value) ? value ?? "" : "";

    private static bool IsChatMessage(OverlayRealtimeEvent evt) =>
        string.Equals(evt.Type, ChatMessageType, StringComparison.Ordinal) &&
        string.Equals(evt.Source, "twitch", StringComparison.OrdinalIgnoreCase);
}
