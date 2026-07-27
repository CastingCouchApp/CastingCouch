using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

/// <summary>
/// Thin Streamer.bot WebSocket client for connection status, action list, and execute.
/// Event-listener sockets remain owned by the UI layer when needed.
/// </summary>
public sealed class StreamerBotClient : IStreamerBotClient, IAsyncDisposable
{
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Lock _sync = new();
    private ClientWebSocket? _socket;
    private StreamerBotConnectionInfo? _connection;

    public StreamerBotConnectionStatus Status
    {
        get
        {
            lock (_sync)
            {
                bool connected = IsConnected;
                string host = _connection?.Host ?? "";
                int port = _connection?.Port ?? 0;
                return new StreamerBotConnectionStatus(
                    connected,
                    host,
                    port,
                    connected
                        ? $"Verbunden · {host}:{port}"
                        : "Nicht verbunden");
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _socket is { State: WebSocketState.Open };
            }
        }
    }

    public StreamerBotConnectionInfo ResolveConnection(StreamerBotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string host = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host.Trim();
        int port = settings.Port is > 0 and <= 65535 ? settings.Port : 8080;
        string endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? "/" : settings.Endpoint.Trim();
        if (!endpoint.StartsWith('/'))
        {
            endpoint = "/" + endpoint;
        }

        string password = settings.Password ?? "";
        if (!string.IsNullOrWhiteSpace(password))
        {
            string separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            endpoint += separator + "password=" + Uri.EscapeDataString(password);
        }

        var uri = new Uri($"ws://{host}:{port}{endpoint}");
        return new StreamerBotConnectionInfo(host, port, endpoint, password, uri);
    }

    public async Task ConnectAsync(
        StreamerBotSettings settings,
        CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken);

        StreamerBotConnectionInfo connection = ResolveConnection(settings);
        var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(connection.Password))
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + connection.Password);
        }

        await socket.ConnectAsync(connection.WebSocketUri, cancellationToken);

        lock (_sync)
        {
            _socket = socket;
            _connection = connection;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ClientWebSocket? socket;
        lock (_sync)
        {
            socket = _socket;
            _socket = null;
            _connection = null;
        }

        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "disconnect",
                    cancellationToken);
            }
        }
        catch
        {
            // Best-effort disconnect.
        }
        finally
        {
            socket.Dispose();
        }
    }

    public async Task<JsonDocument> SendRequestAsync(
        object requestBody,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ClientWebSocket socket;
        lock (_sync)
        {
            if (_socket is null || _socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
            }

            socket = _socket;
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            string id = "ccs-" + Guid.NewGuid().ToString("N");
            string json = JsonSerializer.Serialize(requestBody);
            using var bodyDocument = JsonDocument.Parse(json);
            var dictionary = bodyDocument.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
            dictionary["id"] = JsonDocument.Parse(JsonSerializer.Serialize(id)).RootElement.Clone();
            string payload = JsonSerializer.Serialize(dictionary);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
            byte[] buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeoutCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Streamer.bot hat die WebSocket-Verbindung geschlossen.");
                }

                stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var response = JsonDocument.Parse(stream.ToArray());
                if (!response.RootElement.TryGetProperty("id", out JsonElement responseId) ||
                    !string.Equals(responseId.GetString(), id, StringComparison.Ordinal))
                {
                    response.Dispose();
                    stream.SetLength(0);
                    continue;
                }

                if (response.RootElement.TryGetProperty("status", out JsonElement status) &&
                    string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                {
                    string? message = response.RootElement.TryGetProperty("message", out JsonElement messageNode)
                        ? messageNode.GetString()
                        : "Unbekannter Streamer.bot-Fehler";
                    response.Dispose();
                    throw new InvalidOperationException(message);
                }

                return response;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<IReadOnlyList<StreamerBotActionInfo>> GetActionsAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await SendRequestAsync(
            new { request = "GetActions" },
            TimeSpan.FromSeconds(5),
            cancellationToken);

        if (!response.RootElement.TryGetProperty("actions", out JsonElement actionsNode) ||
            actionsNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<StreamerBotActionInfo>();
        foreach (JsonElement action in actionsNode.EnumerateArray())
        {
            string id = action.TryGetProperty("id", out JsonElement idNode) ? idNode.GetString() ?? "" : "";
            string name = action.TryGetProperty("name", out JsonElement nameNode) ? nameNode.GetString() ?? "" : "";
            string group = action.TryGetProperty("group", out JsonElement groupNode) ? groupNode.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string display = string.IsNullOrWhiteSpace(group)
                ? name
                : $"{group} · {name}";
            results.Add(new StreamerBotActionInfo(id, name, group, display));
        }

        return results;
    }

    public async Task ExecuteActionAsync(
        string? actionId,
        string? actionName,
        object? args = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) && string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("actionId oder actionName erforderlich.");
        }

        var action = !string.IsNullOrWhiteSpace(actionId)
            ? new { id = actionId, name = actionName ?? "" }
            : new { id = "", name = actionName ?? "" };

        using JsonDocument response = await SendRequestAsync(
            new
            {
                request = "DoAction",
                action,
                args = args ?? new { }
            },
            cancellationToken: cancellationToken);

        string? status = response.RootElement.TryGetProperty("status", out JsonElement statusNode)
            ? statusNode.GetString()
            : null;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _requestGate.Dispose();
    }
}
