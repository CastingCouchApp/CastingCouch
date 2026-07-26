using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchEventSubClient : ITwitchEventSubClient
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;

    public bool IsConnected =>
        _socket is { State: WebSocketState.Open };

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<TwitchChatMessage>? ChatMessageReceived;
    public event EventHandler<TwitchEvent>? EventReceived;

    public async Task ConnectAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        string userId,
        bool enableChat,
        bool enableEvents,
        CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken);

        _socket = new ClientWebSocket();

        await _socket.ConnectAsync(
            new Uri(TwitchConstants.EventSubWebSocketUrl),
            cancellationToken);

        var welcome = await ReceiveDocumentAsync(
            _socket,
            cancellationToken);

        var metadata = welcome.RootElement.GetProperty("metadata");
        var messageType = metadata
            .GetProperty("message_type")
            .GetString();

        if (!string.Equals(
                messageType,
                "session_welcome",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Twitch EventSub sendete keine session_welcome-Nachricht.");
        }

        var sessionId = welcome.RootElement
            .GetProperty("payload")
            .GetProperty("session")
            .GetProperty("id")
            .GetString()
            ?? throw new InvalidOperationException(
                "Twitch EventSub Session-ID fehlt.");

        var activeSubscriptions = 0;

        if (enableChat)
        {
            if (await TryCreateSubscriptionAsync(
                    apiClient,
                    "channel.chat.message",
                    "1",
                    new
                    {
                        broadcaster_user_id = broadcasterUserId,
                        user_id = userId
                    },
                    sessionId,
                    "Twitch-Chat",
                    cancellationToken))
            {
                activeSubscriptions++;
            }
        }

        if (enableEvents)
        {
            activeSubscriptions += await SubscribeEventsAsync(
                apiClient,
                broadcasterUserId,
                userId,
                sessionId,
                cancellationToken);
        }

        if (activeSubscriptions == 0 && (enableChat || enableEvents))
        {
            throw new InvalidOperationException(
                "Twitch EventSub konnte keine Chat- oder Event-Abonnements anlegen. " +
                "Bitte Twitch erneut autorisieren und die benötigten Berechtigungen bestätigen.");
        }

        _receiveCancellation = new CancellationTokenSource();
        _receiveTask = Task.Run(
            () => ReceiveLoopAsync(
                apiClient,
                broadcasterUserId,
                userId,
                _receiveCancellation.Token),
            CancellationToken.None);

        ConnectionStateChanged?.Invoke(this, true);
    }

    public async Task DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        _receiveCancellation?.Cancel();

        if (_socket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
        {
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnect",
                    cancellationToken);
            }
            catch
            {
                _socket.Abort();
            }
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
            }
        }

        _receiveCancellation?.Dispose();
        _receiveCancellation = null;
        _receiveTask = null;
        _socket?.Dispose();
        _socket = null;

        ConnectionStateChanged?.Invoke(this, false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private async Task<int> SubscribeEventsAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var subscriptions = new[]
        {
            new Subscription(
                "channel.follow",
                "2",
                new
                {
                    broadcaster_user_id = broadcasterUserId,
                    moderator_user_id = userId
                }),

            new Subscription(
                "channel.subscribe",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "channel.subscription.message",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "channel.subscription.gift",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "channel.cheer",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "channel.raid",
                "1",
                new
                {
                    to_broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "channel.guest_star_guest.update",
                "beta",
                new
                {
                    broadcaster_user_id = broadcasterUserId,
                    moderator_user_id = broadcasterUserId
                }),

            new Subscription(
                "stream.online",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                }),

            new Subscription(
                "stream.offline",
                "1",
                new
                {
                    broadcaster_user_id = broadcasterUserId
                })
        };

        var activeSubscriptions = 0;

        foreach (var subscription in subscriptions)
        {
            if (await TryCreateSubscriptionAsync(
                    apiClient,
                    subscription.Type,
                    subscription.Version,
                    subscription.Condition,
                    sessionId,
                    subscription.Type,
                    cancellationToken))
            {
                activeSubscriptions++;
            }
        }

        return activeSubscriptions;
    }

    private async Task<bool> TryCreateSubscriptionAsync(
        ITwitchApiClient apiClient,
        string type,
        string version,
        object condition,
        string sessionId,
        string displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.CreateEventSubSubscriptionAsync(
                type,
                version,
                condition,
                sessionId,
                cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            EventReceived?.Invoke(
                this,
                new TwitchEvent(
                    "subscription.warning",
                    $"{displayName} konnte nicht aktiviert werden: {exception.Message}",
                    DateTimeOffset.Now,
                    new Dictionary<string, string>
                    {
                        ["subscription_type"] = type,
                        ["error"] = exception.Message
                    }));

            return false;
        }
    }

    private async Task ReceiveLoopAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        string userId,
        CancellationToken cancellationToken)
    {
        var socket = _socket
                     ?? throw new InvalidOperationException(
                         "Twitch EventSub ist nicht initialisiert.");

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State == WebSocketState.Open)
            {
                using var document = await ReceiveDocumentAsync(
                    socket,
                    cancellationToken);

                var root = document.RootElement;
                var metadata = root.GetProperty("metadata");
                var messageType = metadata
                    .GetProperty("message_type")
                    .GetString();

                switch (messageType)
                {
                    case "notification":
                        HandleNotification(root);
                        break;

                    case "session_reconnect":
                    {
                        var reconnectUrl = root
                            .GetProperty("payload")
                            .GetProperty("session")
                            .GetProperty("reconnect_url")
                            .GetString();

                        if (!string.IsNullOrWhiteSpace(reconnectUrl))
                        {
                            await ReconnectAsync(
                                apiClient,
                                broadcasterUserId,
                                userId,
                                reconnectUrl,
                                cancellationToken);
                        }

                        break;
                    }

                    case "revocation":
                        EventReceived?.Invoke(
                            this,
                            new TwitchEvent(
                                "revocation",
                                "Eine Twitch EventSub-Subscription wurde widerrufen.",
                                DateTimeOffset.Now,
                                new Dictionary<string, string>()));
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    private void HandleNotification(JsonElement root)
    {
        var payload = root.GetProperty("payload");
        var subscription = payload.GetProperty("subscription");
        var eventType = subscription.GetProperty("type").GetString() ?? "";
        var eventData = payload.GetProperty("event");

        if (string.Equals(
                eventType,
                "channel.chat.message",
                StringComparison.Ordinal))
        {
            var message = eventData
                .GetProperty("message")
                .GetProperty("text")
                .GetString()
                ?? "";

            var badges =
                eventData.TryGetProperty("badges", out var badgesElement) &&
                badgesElement.ValueKind == JsonValueKind.Array
                    ? badgesElement
                        .EnumerateArray()
                        .Select(
                            badge =>
                                GetString(
                                    badge,
                                    "set_id"))
                        .Where(
                            badge =>
                                !string.IsNullOrWhiteSpace(
                                    badge))
                        .ToArray()
                    : Array.Empty<string>();

            var chatMessage = new TwitchChatMessage(
                GetString(eventData, "message_id"),
                GetString(eventData, "broadcaster_user_id"),
                GetString(eventData, "chatter_user_id"),
                GetString(eventData, "chatter_user_login"),
                GetString(eventData, "chatter_user_name"),
                message,
                GetString(eventData, "color"),
                DateTimeOffset.Now,
                badges);

            ChatMessageReceived?.Invoke(
                this,
                chatMessage);

            return;
        }

        var data = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var property in eventData.EnumerateObject())
        {
            data[property.Name] =
                property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString();
        }

        EventReceived?.Invoke(
            this,
            new TwitchEvent(
                eventType,
                CreateSummary(eventType, data),
                DateTimeOffset.Now,
                data));
    }

    private async Task ReconnectAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        string userId,
        string reconnectUrl,
        CancellationToken cancellationToken)
    {
        var replacement = new ClientWebSocket();

        await replacement.ConnectAsync(
            new Uri(reconnectUrl),
            cancellationToken);

        using var welcome = await ReceiveDocumentAsync(
            replacement,
            cancellationToken);

        var newSocket = replacement;
        var oldSocket = _socket;
        _socket = newSocket;

        if (oldSocket is not null)
        {
            try
            {
                await oldSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "EventSub reconnect",
                    cancellationToken);
            }
            catch
            {
                oldSocket.Abort();
            }

            oldSocket.Dispose();
        }
    }

    private static async Task<JsonDocument> ReceiveDocumentAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(
                buffer,
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    "Twitch EventSub hat die Verbindung geschlossen.");
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }

            if (stream.Length > 4 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "Twitch EventSub-Nachricht ist zu groß.");
            }
        }

        stream.Position = 0;

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private static string CreateSummary(
        string eventType,
        IReadOnlyDictionary<string, string> data)
    {
        return eventType switch
        {
            "channel.follow" =>
                $"{Get(data, "user_name")} folgt dem Kanal.",

            "channel.subscribe" =>
                $"{Get(data, "user_name")} hat abonniert.",

            "channel.subscription.message" =>
                $"{Get(data, "user_name")} hat erneut abonniert.",

            "channel.subscription.gift" =>
                $"{Get(data, "user_name")} verschenkt Subs.",

            "channel.cheer" =>
                $"{Get(data, "user_name")} cheeret {Get(data, "bits")} Bits.",

            "channel.raid" =>
                $"{Get(data, "from_broadcaster_user_name")} raidet mit " +
                $"{Get(data, "viewers")} Zuschauern.",

            "channel.guest_star_guest.update" =>
                Get(data, "state") switch
                {
                    "invited" => $"Stream-Together-Anfrage für {Get(data, "guest_user_name")}.",
                    "accepted" => $"{Get(data, "guest_user_name")} hat Stream Together angenommen.",
                    "ready" => $"{Get(data, "guest_user_name")} ist für Stream Together bereit.",
                    "live" => $"{Get(data, "guest_user_name")} ist jetzt in Stream Together live.",
                    "removed" => $"{Get(data, "guest_user_name")} hat Stream Together verlassen.",
                    _ => $"Stream Together: {Get(data, "guest_user_name")} ({Get(data, "state")})."
                },

            "stream.online" =>
                "Der Stream ist online.",

            "stream.offline" =>
                "Der Stream ist offline.",

            _ => eventType
        };
    }

    private static string Get(
        IReadOnlyDictionary<string, string> data,
        string key)
    {
        return data.TryGetValue(key, out var value)
            ? value
            : "";
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? ""
            : "";
    }

    private sealed record Subscription(
        string Type,
        string Version,
        object Condition);
}
