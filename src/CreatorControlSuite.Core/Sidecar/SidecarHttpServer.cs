using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CreatorControlSuite.Core.Sidecar;

public sealed class SidecarHttpServer : IAsyncDisposable
{
    public const int DefaultPort = 18765;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SidecarHttpServer(int port = DefaultPort)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Ungültiger Sidecar-Port.");
        }

        Port = port;
    }

    public int Port { get; }

    public bool IsRunning { get; private set; }

    public static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        listener.Start();

        _listener = listener;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        _loop = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // ignore
        }

        if (_loop is not null)
        {
            try { await _loop; }
            catch { /* ignore */ }
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync()
        => await StopAsync();

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context), CancellationToken.None);
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
            string method = context.Request.HttpMethod ?? "GET";

            if (path is "/sidecar/health" &&
                string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 200, new { ok = true });
                return;
            }

            if (path is "/sidecar/ytm/now-playing" &&
                string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 200, new
                {
                    provider = "ytmusic",
                    connected = false,
                    isPlaying = false,
                    title = "",
                    artist = "",
                    album = "",
                    statusText = "Nicht verbunden"
                });
                return;
            }

            if (path is "/sidecar/workflow/run" &&
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                _ = await reader.ReadToEndAsync();
                await WriteJsonAsync(context.Response, 200, new
                {
                    ok = false,
                    message = "Run-of-Show noch nicht im Sidecar"
                });
                return;
            }

            await WriteJsonAsync(context.Response, 404, new { ok = false, message = "Not found" });
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

public static class SidecarCommandLine
{
    public static bool IsSidecarMode(IReadOnlyList<string> args)
        => args.Count > 0 && string.Equals(args[0], "--sidecar", StringComparison.OrdinalIgnoreCase);

    public static int ParsePort(IReadOnlyList<string> args, int fallback = SidecarHttpServer.DefaultPort)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Count &&
                int.TryParse(args[i + 1], out int port) &&
                port is > 0 and <= 65535)
            {
                return port;
            }

            const string prefix = "--port=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i][prefix.Length..], out int inline) &&
                inline is > 0 and <= 65535)
            {
                return inline;
            }
        }

        return fallback;
    }
}
