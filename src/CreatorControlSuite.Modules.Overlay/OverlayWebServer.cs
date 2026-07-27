using System.Net;
using System.Net.WebSockets;
using System.Text;
using CreatorControlSuite.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayWebServer(
    ISettingsStore settingsStore,
    IOverlayDataService overlayData,
    OverlayRealtimeHub realtimeHub) : IOverlayWebServer, IAsyncDisposable
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly IOverlayDataService _overlayData = overlayData;
    private readonly OverlayRealtimeHub _realtimeHub = realtimeHub;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private WebApplication? _app;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private IReadOnlyList<(string Id, string Name, string Url)> _mountedOverlays = [];

    public bool IsRunning => _app is not null && _runTask is { IsCompleted: false };
    public int Port { get; private set; } = 8765;
    public string? BaseUrl { get; private set; }
    public string? RootPath { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
            if (!settings.Overlay.WebServerEnabled)
            {
                return;
            }

            settings.Overlay.EnsureInstancesMigrated();

            string dataRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                await _overlayData.GetOverlayRootAsync(cancellationToken)));
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(Path.Combine(dataRoot, "data"));

            int port = Math.Clamp(settings.Overlay.WebServerPort, 1, 65535);
            Port = port;
            RootPath = dataRoot;
            BaseUrl = settings.Overlay.GetBaseUrl();

            var mounts = new List<(string Id, string Name, string Root, string Url)>();
            foreach (OverlayInstanceSettings instance in settings.Overlay.Instances)
            {
                if (!instance.Enabled || string.IsNullOrWhiteSpace(instance.Id))
                {
                    continue;
                }

                string root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(instance.RootPath.Trim()));
                if (!Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }

                mounts.Add((
                    instance.Id.Trim(),
                    string.IsNullOrWhiteSpace(instance.Name) ? instance.Id.Trim() : instance.Name.Trim(),
                    root,
                    settings.Overlay.GetInstanceUrl(instance.Id)));
            }

            // Legacy: RootPath ohne Instances → am Default-Mount /o/{id}/ bereits via Migration.
            // Zusätzlich RootPath als ContentRoot für data-Routen behalten.
            _mountedOverlays = mounts
                .Select(m => (m.Id, m.Name, m.Url))
                .ToArray();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = dataRoot,
                WebRootPath = dataRoot
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
            });

            WebApplication app = builder.Build();
            _realtimeHub.ConfigureChatBuffer(settings.Overlay.Chat.MaxBufferedMessages);
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            app.Use(async (context, next) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) &&
                    context.Request.Path.Equals("/data/overlay-data.json", StringComparison.OrdinalIgnoreCase))
                {
                    string path = await _overlayData.GetDataFilePathAsync(context.RequestAborted);
                    if (!File.Exists(path))
                    {
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.WriteAsync("{\"updatedAt\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\"}", context.RequestAborted);
                        return;
                    }

                    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.SendFileAsync(path, context.RequestAborted);
                    return;
                }

                if (HttpMethods.IsGet(context.Request.Method) &&
                    context.Request.Path.Equals("/data/overlay-config.json", StringComparison.OrdinalIgnoreCase))
                {
                    string dataPath = await _overlayData.GetDataFilePathAsync(context.RequestAborted);
                    string? directory = Path.GetDirectoryName(dataPath);
                    string path = string.IsNullOrWhiteSpace(directory)
                        ? Path.Combine(dataRoot, "data", "overlay-config.json")
                        : Path.Combine(directory, "overlay-config.json");

                    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    context.Response.ContentType = "application/json; charset=utf-8";
                    if (!File.Exists(path))
                    {
                        await context.Response.WriteAsync("{}", context.RequestAborted);
                        return;
                    }

                    await context.Response.SendFileAsync(path, context.RequestAborted);
                    return;
                }

                await next();
            });

            foreach (var mount in mounts)
            {
                var fileProvider = new PhysicalFileProvider(mount.Root);
                string requestPath = "/o/" + mount.Id;
                app.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = fileProvider,
                    RequestPath = requestPath
                });
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = fileProvider,
                    RequestPath = requestPath,
                    ServeUnknownFileTypes = true,
                    DefaultContentType = "application/octet-stream",
                    OnPrepareResponse = ctx =>
                    {
                        string path = ctx.File.Name;
                        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                        }
                    }
                });
            }

            app.MapGet("/health", () => Results.Json(new
            {
                ok = true,
                port,
                root = dataRoot,
                clients = _realtimeHub.ConnectedClients,
                baseUrl = BaseUrl,
                overlays = _mountedOverlays.Select(o => new { id = o.Id, name = o.Name, url = o.Url }).ToArray()
            }));

            app.MapGet("/chat", async (HttpContext context) =>
            {
                AppSettings chatSettings = await _settingsStore.LoadAsync(context.RequestAborted);
                if (!chatSettings.Overlay.Chat.Enabled)
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    await context.Response.WriteAsync(
                        "<!DOCTYPE html><html><body style=\"background:transparent;color:#fff;font-family:sans-serif\">Chat-Overlay deaktiviert</body></html>",
                        context.RequestAborted);
                    return;
                }

                if (!ChatOverlayAssets.TryGet("index.html", out string html, out string contentType))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = contentType;
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                await context.Response.WriteAsync(html, context.RequestAborted);
            });

            app.MapGet("/chat/config", async (HttpContext context) =>
            {
                AppSettings chatSettings = await _settingsStore.LoadAsync(context.RequestAborted);
                OverlayChatSettings chat = chatSettings.Overlay.Chat ?? new OverlayChatSettings();
                chat.NormalizeAppearance();
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                bool hasImage = chat.BackgroundType == "Image" &&
                    !string.IsNullOrWhiteSpace(chat.BackgroundImagePath) &&
                    File.Exists(Environment.ExpandEnvironmentVariables(chat.BackgroundImagePath));
                return Results.Json(new
                {
                    enabled = chat.Enabled,
                    showTwitchEvents = chat.ShowTwitchEvents,
                    enableBttv = chat.EnableBttv,
                    enableFfz = chat.EnableFfz,
                    enableSevenTv = chat.EnableSevenTv,
                    backgroundType = hasImage || chat.BackgroundType != "Image"
                        ? chat.BackgroundType
                        : "None",
                    backgroundColor = chat.BackgroundColor,
                    backgroundOpacity = chat.BackgroundOpacity,
                    paddingPx = chat.PaddingPx,
                    borderRadiusPx = chat.BorderRadiusPx,
                    gapPx = chat.GapPx,
                    backgroundVersion = hasImage
                        ? File.GetLastWriteTimeUtc(Environment.ExpandEnvironmentVariables(chat.BackgroundImagePath)).Ticks.ToString()
                        : "0"
                });
            });

            app.MapGet("/chat/background", async (HttpContext context) =>
            {
                AppSettings chatSettings = await _settingsStore.LoadAsync(context.RequestAborted);
                OverlayChatSettings chat = chatSettings.Overlay.Chat ?? new OverlayChatSettings();
                chat.NormalizeAppearance();
                if (chat.BackgroundType != "Image" || string.IsNullOrWhiteSpace(chat.BackgroundImagePath))
                {
                    return Results.NotFound();
                }

                string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(chat.BackgroundImagePath));
                if (!File.Exists(path))
                {
                    return Results.NotFound();
                }

                string contentType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "application/octet-stream"
                };

                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                return Results.File(path, contentType);
            });

            app.MapGet("/chat/{fileName}", (string fileName, HttpContext context) =>
            {
                string safe = Path.GetFileName(fileName);
                if (string.Equals(safe, "config", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(safe, "background", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound();
                }

                if (!ChatOverlayAssets.TryGet(safe, out string content, out string contentType))
                {
                    return Results.NotFound();
                }

                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                return Results.Content(content, contentType);
            });

            app.Map("/ws", HandleWebSocketAsync);

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _app = app;
            _runTask = app.RunAsync(_runCts.Token);

            await Task.Delay(50, cancellationToken);
            if (_runTask.IsFaulted)
            {
                Exception? error = _runTask.Exception?.GetBaseException();
                await StopCoreAsync();
                throw new InvalidOperationException(
                    "Overlay-Webserver konnte nicht gestartet werden: " + (error?.Message ?? "unbekannter Fehler"),
                    error);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }

    private async Task StopCoreAsync()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_app is not null)
        {
            try
            {
                await _app.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // ignore
            }

            await _app.DisposeAsync();
        }

        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch
            {
                // ignore cancelled/faulted run
            }
        }

        _app = null;
        _runTask = null;
        _runCts?.Dispose();
        _runCts = null;
        BaseUrl = null;
        RootPath = null;
        _mountedOverlays = [];
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        Guid id = Guid.NewGuid();

        async Task SendAsync(string json, CancellationToken ct)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        _realtimeHub.Register(id, SendAsync);

        try
        {
            OverlayRealtimeEvent hello = OverlayEventBridge.AppWsHello(
                _realtimeHub.ConnectedClients,
                _mountedOverlays.Select(o => (o.Id, o.Name)).ToArray());
            await SendAsync(_realtimeHub.SerializeEvent(hello), context.RequestAborted);

            AppSettings liveSettings = await _settingsStore.LoadAsync(context.RequestAborted);
            if (liveSettings.Overlay.Chat.Enabled)
            {
                foreach (OverlayRealtimeEvent chatEvent in _realtimeHub.GetBufferedChatEvents())
                {
                    await SendAsync(_realtimeHub.SerializeEvent(chatEvent), context.RequestAborted);
                }
            }

            var buffer = new byte[4 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (WebSocketException)
        {
            // client gone
        }
        finally
        {
            _realtimeHub.Unregister(id);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "bye",
                        CancellationToken.None);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
