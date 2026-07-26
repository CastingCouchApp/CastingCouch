using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Modules.YouTubeMusic;

public sealed class YouTubeMusicBridge : IAsyncDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly object _stateLock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private YouTubeMusicBridgeState _state = YouTubeMusicBridgeState.Empty;
    private DateTimeOffset _lastStateAt = DateTimeOffset.MinValue;
    private bool _running;

    public YouTubeMusicBridge(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public bool IsRunning => _running;

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
            return;

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var port = settings.YouTubeMusic.BridgePort;
        if (port is <= 0 or > 65535)
            throw new InvalidOperationException("Ungültiger YouTube-Music-Bridge-Port.");

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _listener = listener;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;
        _loop = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (!_running)
            return;

        _running = false;
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

        lock (_stateLock)
        {
            _state = YouTubeMusicBridgeState.Empty;
            _lastStateAt = DateTimeOffset.MinValue;
        }

        while (_commands.TryDequeue(out _)) { }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnqueueCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        _commands.Enqueue(command.Trim().ToLowerInvariant());
    }

    public NowPlayingSnapshot GetSnapshot(int stateTimeoutSeconds)
    {
        YouTubeMusicBridgeState state;
        DateTimeOffset lastStateAt;
        lock (_stateLock)
        {
            state = _state;
            lastStateAt = _lastStateAt;
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(stateTimeoutSeconds, 3, 120));
        var fresh = lastStateAt != DateTimeOffset.MinValue &&
                    DateTimeOffset.UtcNow - lastStateAt <= timeout;
        var connected = _running && fresh;

        if (!connected)
        {
            return new NowPlayingSnapshot(
                MusicProviderIds.YouTubeMusic,
                Connected: _running,
                IsPlaying: false,
                Title: "",
                Artist: "",
                Album: "",
                CoverUrl: "",
                ProgressMs: 0,
                DurationMs: 0,
                VolumePercent: null,
                StatusText: !_running
                    ? "Bridge gestoppt"
                    : "Bookmarklet inaktiv");
        }

        return new NowPlayingSnapshot(
            MusicProviderIds.YouTubeMusic,
            Connected: true,
            IsPlaying: state.IsPlaying,
            Title: state.Title,
            Artist: state.Artist,
            Album: state.Album,
            CoverUrl: state.CoverUrl,
            ProgressMs: Math.Max(0, state.ProgressMs),
            DurationMs: Math.Max(0, state.DurationMs),
            VolumePercent: null,
            StatusText: string.IsNullOrWhiteSpace(state.Title)
                ? "Verbunden · Kein Titel"
                : state.IsPlaying ? "Spielt" : "Pause");
    }

    public string GetBookmarklet(int port)
    {
        // music.youtube.com erzwingt Trusted Types (require-trusted-types-for 'script').
        // Script-Tags mit .src/.text sind blockiert – daher Bridge-Code inline im Bookmarklet.
        // Newlines müssen erhalten bleiben (als %0A): sonst kommentiert das erste // den Rest der Zeile weg.
        var script = GetBridgeScript(port).Trim();
        return "javascript:" + Uri.EscapeDataString(script);
    }

    public string GetBookmarkletInstallPageUrl(int port)
        => $"http://127.0.0.1:{port}/ytmusic/install";

    public string GetBookmarkletDisplayName()
        => "CCS · YouTube Music";

    public string GetBookmarkletInstallHtml(int port)
    {
        var bookmarklet = GetBookmarklet(port);
        var title = GetBookmarkletDisplayName();
        var href = System.Net.WebUtility.HtmlEncode(bookmarklet);
        var text = System.Net.WebUtility.HtmlEncode(title);
        return $$"""
            <!DOCTYPE html>
            <html lang="de">
            <head>
              <meta charset="utf-8"/>
              <title>{{text}} – Bookmarklet</title>
              <style>
                body{font-family:Segoe UI,sans-serif;background:#0b1014;color:#e8eef2;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center}
                .card{background:#151c22;border:1px solid #2a343c;border-radius:14px;padding:28px;max-width:520px;text-align:center;box-shadow:0 12px 40px rgba(0,0,0,.35)}
                h1{font-size:22px;margin:0 0 10px}
                p{color:#9aa6ae;line-height:1.45;margin:0 0 22px}
                a.drag{display:inline-block;padding:14px 22px;border-radius:999px;background:#ff6a00;color:#fff;font-weight:700;text-decoration:none;cursor:grab;user-select:none;border:1px solid #ff8a33}
                a.drag:active{cursor:grabbing}
                .hint{margin-top:18px;font-size:13px;color:#7f8991}
              </style>
            </head>
            <body>
              <div class="card">
                <h1>YouTube Music Bookmarklet</h1>
                <p>Ziehe den orangenen Link in die Lesezeichenleiste deines Browsers. Danach auf <strong>music.youtube.com</strong> einmal anklicken und den Tab offen lassen.</p>
                <a class="drag" href="{{href}}">{{text}}</a>
                <div class="hint">Tipp: Lesezeichenleiste mit Strg+Shift+B einblenden. Nach App-Updates Bookmarklet neu ziehen (läuft inline wegen YouTube Trusted Types).</div>
              </div>
            </body>
            </html>
            """;
    }

    public string GetBridgeScript(int port)
    {
        var raw = LoadEmbeddedBridgeScript();
        return raw.Replace("__CCS_BRIDGE_PORT__", port.ToString(), StringComparison.Ordinal);
    }

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

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            AddCors(context.Response);
            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
            if (path is "/ytmusic/state" &&
                string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var incoming = JsonSerializer.Deserialize<YouTubeMusicBridgeState>(body, JsonOptions())
                    ?? YouTubeMusicBridgeState.Empty;
                lock (_stateLock)
                {
                    _state = incoming;
                    _lastStateAt = DateTimeOffset.UtcNow;
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
                await WriteJsonAsync(context.Response, 200, new { ok = true });
                return;
            }

            if (path is "/ytmusic/commands" &&
                string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var commands = new List<string>();
                while (_commands.TryDequeue(out var command))
                    commands.Add(command);

                await WriteJsonAsync(context.Response, 200, new { commands });
                return;
            }

            if (path is "/ytmusic/bookmarklet.js" &&
                string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var settings = await _settingsStore.LoadAsync();
                var script = GetBridgeScript(settings.YouTubeMusic.BridgePort);
                var bytes = Encoding.UTF8.GetBytes(script);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/javascript; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
                return;
            }

            if (path is "/ytmusic/install" &&
                string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var settings = await _settingsStore.LoadAsync();
                var html = GetBookmarkletInstallHtml(settings.YouTubeMusic.BridgePort);
                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
                return;
            }

            if (path is "/ytmusic/health")
            {
                await WriteJsonAsync(context.Response, 200, new { ok = true, running = _running });
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
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

    private static void AddCors(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions());
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private static string LoadEmbeddedBridgeScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("ytmusic-bridge.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ytmusic-bridge.js fehlt als EmbeddedResource.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("ytmusic-bridge.js konnte nicht geladen werden.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public async ValueTask DisposeAsync()
        => await StopAsync();
}

public sealed class YouTubeMusicBridgeState
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public bool IsPlaying { get; set; }
    public int ProgressMs { get; set; }
    public int DurationMs { get; set; }

    public static YouTubeMusicBridgeState Empty { get; } = new();
}
