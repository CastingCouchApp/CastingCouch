using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public interface ITwitchEventSubClient : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<TwitchChatMessage>? ChatMessageReceived;
    event EventHandler<TwitchEvent>? EventReceived;

    Task ConnectAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        string userId,
        bool enableChat,
        bool enableEvents,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        CancellationToken cancellationToken = default);
}
