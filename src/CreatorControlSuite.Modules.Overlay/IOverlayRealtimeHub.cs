namespace CreatorControlSuite.Modules.Overlay;

public interface IOverlayRealtimeHub
{
    int ConnectedClients { get; }

    event Action? ChatBufferChanged;

    void ConfigureChatBuffer(int maxMessages);
    IReadOnlyList<OverlayRealtimeEvent> GetBufferedChatEvents();
    void RestoreBufferedChatEvents(IEnumerable<OverlayRealtimeEvent> events);
    void ClearBufferedChat();
    bool RemoveBufferedChatMessage(string messageId);
    int RemoveBufferedChatMessagesByUser(string userLogin, string userId);
    string SerializeEvent(OverlayRealtimeEvent evt);

    void Register(Guid id, Func<string, CancellationToken, Task> sendAsync);
    void Unregister(Guid id);
    Task PublishEventAsync(OverlayRealtimeEvent? evt, CancellationToken cancellationToken = default);
}
