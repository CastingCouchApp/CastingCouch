using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public sealed record StreamerBotConnectionInfo(
    string Host,
    int Port,
    string Endpoint,
    string Password,
    Uri WebSocketUri);

public sealed record StreamerBotConnectionStatus(
    bool IsConnected,
    string Host,
    int Port,
    string Detail);

public sealed record StreamerBotActionInfo(
    string Id,
    string Name,
    string Group,
    string DisplayName);

public interface IStreamerBotClient
{
    StreamerBotConnectionStatus Status { get; }
    bool IsConnected { get; }

    StreamerBotConnectionInfo ResolveConnection(StreamerBotSettings settings);

    Task ConnectAsync(StreamerBotSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument> SendRequestAsync(
        object requestBody,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StreamerBotActionInfo>> GetActionsAsync(
        CancellationToken cancellationToken = default);

    Task ExecuteActionAsync(
        string? actionId,
        string? actionName,
        object? args = null,
        CancellationToken cancellationToken = default);
}
