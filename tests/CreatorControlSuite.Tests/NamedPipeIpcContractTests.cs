using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;

namespace CreatorControlSuite.Tests;

public sealed class NamedPipeIpcContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task RoundTrip_PreservesCommandAndResponseContract()
    {
        IpcCommand? received = null;
        var router = new DelegateRouter((command, _) =>
        {
            received = command;
            return Task.FromResult(new IpcResponse(
                command.Id,
                true,
                "pong",
                new Dictionary<string, string>
                {
                    ["state"] = "ready"
                }));
        });
        await using var server = new NamedPipeIpcServer(router, new NullLogger());
        await server.StartAsync();
        var command = new IpcCommand(
            "request-1",
            IpcCommandNames.Ping,
            new Dictionary<string, string>
            {
                ["origin"] = "contract-test"
            },
            DateTimeOffset.UtcNow);

        IpcResponse response = await SendAsync(
            JsonSerializer.Serialize(command));

        Assert.NotNull(received);
        Assert.Equal(command.Id, received.Id);
        Assert.Equal(command.Command, received.Command);
        Assert.Equal("contract-test", received.Arguments["origin"]);
        Assert.True(response.Success);
        Assert.Equal(command.Id, response.Id);
        Assert.Equal("pong", response.Message);
        Assert.Equal("ready", response.Data["state"]);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task MalformedRequest_ReturnsFailureAndDoesNotPoisonServer()
    {
        var router = new DelegateRouter((command, _) =>
            Task.FromResult(new IpcResponse(
                command.Id,
                true,
                "accepted",
                new Dictionary<string, string>())));
        await using var server = new NamedPipeIpcServer(router, new NullLogger());
        await server.StartAsync();

        IpcResponse malformed = await SendAsync("{not-json");
        IpcResponse valid = await SendAsync(JsonSerializer.Serialize(
            new IpcCommand(
                "request-2",
                IpcCommandNames.Status,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow)));

        Assert.False(malformed.Success);
        Assert.NotEmpty(malformed.Id);
        Assert.True(valid.Success);
        Assert.Equal("request-2", valid.Id);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task Lifecycle_IsIdempotentAndPublishesStateChanges()
    {
        var router = new DelegateRouter((command, _) =>
            Task.FromResult(new IpcResponse(
                command.Id,
                true,
                "ok",
                new Dictionary<string, string>())));
        await using var server = new NamedPipeIpcServer(router, new NullLogger());
        var states = new List<bool>();
        server.StateChanged += (_, running) => states.Add(running);

        await server.StartAsync();
        await server.StartAsync();
        await server.StopAsync();
        await server.StopAsync();

        Assert.False(server.IsRunning);
        Assert.Equal([true, false], states);
    }

    private static async Task<IpcResponse> SendAsync(string request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(
            ".",
            NamedPipeIpcServer.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            false,
            4096,
            true);
        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            4096,
            true)
        {
            AutoFlush = true
        };

        await writer.WriteLineAsync(request.AsMemory(), timeout.Token);
        string? line = await reader.ReadLineAsync(timeout.Token);

        Assert.False(string.IsNullOrWhiteSpace(line));
        return JsonSerializer.Deserialize<IpcResponse>(line)
            ?? throw new InvalidOperationException(
                "IPC-Antwort war leer oder ungültig.");
    }

    private sealed class DelegateRouter(
        Func<IpcCommand, CancellationToken, Task<IpcResponse>> execute)
        : IIpcCommandRouter
    {
        public Task<IpcResponse> ExecuteAsync(
            IpcCommand command,
            CancellationToken cancellationToken = default) =>
            execute(command, cancellationToken);
    }

    private sealed class NullLogger : IAppLogger
    {
        public event EventHandler<AppLogEntry>? EntryWritten
        {
            add { }
            remove { }
        }

        public void Write(
            AppLogLevel level,
            string category,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }

        public Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
            int maxEntries = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppLogEntry>>([]);

        public Task<string> ExportAsync(
            string targetPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(targetPath);
    }
}
