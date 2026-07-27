namespace CreatorControlSuite.Modules.Overlay;

public interface IOverlayRealtimeHub
{
    int ConnectedClients { get; }

    void ConfigureChatBuffer(int maxMessages);
    IReadOnlyList<OverlayRealtimeEvent> GetBufferedChatEvents();
    string SerializeEvent(OverlayRealtimeEvent evt);

    void Register(Guid id, Func<string, CancellationToken, Task> sendAsync);
    void Unregister(Guid id);
    Task PublishEventAsync(OverlayRealtimeEvent? evt, CancellationToken cancellationToken = default);
}
