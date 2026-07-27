using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayWebServer(
    ISettingsStore settingsStore,
    IOverlayDataService overlayData,
    IOverlayLayoutStore layoutStore,
    OverlayRealtimeHub realtimeHub,
    IOverlayExtensionStore extensionStore,
    IObsWebSocketClient obsClient) : IOverlayWebServer, IAsyncDisposable
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly IOverlayDataService _overlayData = overlayData;
    private readonly IOverlayLayoutStore _layoutStore = layoutStore;
    private readonly OverlayRealtimeHub _realtimeHub = realtimeHub;
    private readonly IOverlayExtensionStore _extensionStore = extensionStore;
    private readonly IObsWebSocketClient _obsClient = obsClient;
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
            settings.Overlay.EnsureCanvasesMigrated();

            string dataRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                await _overlayData.GetOverlayRootAsync(cancellationToken)));
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(Path.Combine(dataRoot, "data"));

            int port = Math.Clamp(settings.Overlay.WebServerPort, 1, 65535);
            Port = port;
            RootPath = dataRoot;
            BaseUrl = settings.Overlay.GetBaseUrl();
            _mountedOverlays = settings.Overlay.Canvases
                .Select(c => (c.Id, c.Name, settings.Overlay.GetViewUrl(c.Id)))
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

            app.MapGet("/health", async (HttpContext context) =>
            {
                AppSettings live = await _settingsStore.LoadAsync(context.RequestAborted);
                live.Overlay.EnsureCanvasesMigrated();
                OverlayCanvasSettings selected = live.Overlay.GetSelectedCanvas();
                // Port/BaseUrl aus laufendem Server; Canvas-URLs mit aktuellem Settings-Port.
                live.Overlay.WebServerPort = Port;
                return Results.Json(new
                {
                    ok = true,
                    port = Port,
                    root = dataRoot,
                    clients = _realtimeHub.ConnectedClients,
                    baseUrl = BaseUrl,
                    canvasId = selected.Id,
                    canvases = live.Overlay.Canvases
                        .Select(c => new
                        {
                            id = c.Id,
                            name = c.Name,
                            editorUrl = live.Overlay.GetEditorUrl(c.Id),
                            viewUrl = live.Overlay.GetViewUrl(c.Id)
                        })
                        .ToArray(),
                    editorUrl = live.Overlay.GetEditorUrl(selected.Id),
                    viewUrl = live.Overlay.GetViewUrl(selected.Id),
                    widgets = CanvasOverlayAssets.ListWidgetTypes()
                        .Select(t => new { type = t, url = live.Overlay.GetWidgetUrl(t) })
                        .Concat(CanvasOverlayAssets.ListShapeTypes()
                            .Select(t => new { type = t, url = live.Overlay.GetWidgetUrl("shape/" + t) }))
                        .ToArray()
                });
            });

            MapCanvasRoutes(app);
            MapExtensionRoutes(app);

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
                    fontSizePx = chat.FontSizePx,
                    fontFamily = chat.FontFamily,
                    backgroundVersion = hasImage
                        ? File.GetLastWriteTimeUtc(Environment.ExpandEnvironmentVariables(chat.BackgroundImagePath)).Ticks.ToString()
                        : "0"
                });
            });

            app.MapGet("/chat/history", async (HttpContext context) =>
            {
                AppSettings chatSettings = await _settingsStore.LoadAsync(context.RequestAborted);
                OverlayChatSettings chat = chatSettings.Overlay.Chat ?? new OverlayChatSettings();
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                if (!chat.Enabled)
                {
                    return Results.Json(new { events = Array.Empty<OverlayRealtimeEvent>() });
                }

                return Results.Json(new { events = _realtimeHub.GetBufferedChatEvents() });
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
                    string.Equals(safe, "background", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(safe, "history", StringComparison.OrdinalIgnoreCase))
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

    public async Task RefreshMountedCanvasesAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        settings.Overlay.EnsureCanvasesMigrated();
        settings.Overlay.WebServerPort = Port > 0 ? Port : settings.Overlay.WebServerPort;
        _mountedOverlays = settings.Overlay.Canvases
            .Select(c => (c.Id, c.Name, settings.Overlay.GetViewUrl(c.Id)))
            .ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }

    private void MapCanvasRoutes(WebApplication app)
    {
        app.MapGet("/editor", async (HttpContext context) =>
        {
            AppSettings live = await _settingsStore.LoadAsync(context.RequestAborted);
            live.Overlay.EnsureCanvasesMigrated();
            string id = live.Overlay.GetSelectedCanvas().Id;
            context.Response.Redirect("/editor/" + Uri.EscapeDataString(id), permanent: false);
        });

        app.MapGet("/view", async (HttpContext context) =>
        {
            AppSettings live = await _settingsStore.LoadAsync(context.RequestAborted);
            live.Overlay.EnsureCanvasesMigrated();
            string id = live.Overlay.GetSelectedCanvas().Id;
            context.Response.Redirect("/view/" + Uri.EscapeDataString(id), permanent: false);
        });

        app.MapGet("/editor/{instanceId}", (string instanceId, HttpContext context) =>
            ServeCanvasPage("editor/index.html", context));

        app.MapGet("/view/{instanceId}", (string instanceId, HttpContext context) =>
            ServeCanvasPage("view/index.html", context));

        app.MapGet("/w/{type}", (string type, HttpContext context) =>
            ServeCanvasPage("solo/index.html", context));

        app.MapGet("/w/shape/{*shapeId}", (string shapeId, HttpContext context) =>
            ServeCanvasPage("solo/index.html", context));

        app.MapGet("/canvas/size-presets", () =>
        {
            return Results.Json(OverlayCanvasSizePresets.All.Select(p => new
            {
                id = p.Id,
                label = p.Label,
                width = p.Width,
                height = p.Height
            }));
        });

        app.MapGet("/obs/video-settings", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            if (!_obsClient.IsConnected)
            {
                return Results.Json(new
                {
                    connected = false,
                    baseWidth = 0,
                    baseHeight = 0,
                    outputWidth = 0,
                    outputHeight = 0
                });
            }

            try
            {
                var video = await _obsClient.GetVideoSettingsAsync(context.RequestAborted);
                return Results.Json(new
                {
                    connected = true,
                    baseWidth = video.BaseWidth,
                    baseHeight = video.BaseHeight,
                    outputWidth = video.OutputWidth,
                    outputHeight = video.OutputHeight
                });
            }
            catch (Exception)
            {
                return Results.Json(new
                {
                    connected = false,
                    baseWidth = 0,
                    baseHeight = 0,
                    outputWidth = 0,
                    outputHeight = 0
                });
            }
        });

        app.MapGet("/obs/preview", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            if (!_obsClient.IsConnected)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                string scene = await _obsClient.GetCurrentProgramSceneAsync(context.RequestAborted);
                if (string.IsNullOrWhiteSpace(scene))
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                byte[] image = await _obsClient.GetSourceScreenshotAsync(
                    scene,
                    imageWidth: 960,
                    imageHeight: null,
                    context.RequestAborted);
                if (image.Length == 0)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                return Results.File(image, "image/png");
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.MapGet("/canvas/{*assetPath}", (string assetPath, HttpContext context) =>
        {
            string safe = (assetPath ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(safe) || safe.Contains("..", StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            if (!CanvasOverlayAssets.TryGet(safe, out string content, out string contentType))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Results.Content(content, contentType);
        });

        app.MapGet("/layout/{instanceId}", async (string instanceId, HttpContext context) =>
        {
            try
            {
                Models.OverlayLayout layout = await _layoutStore.LoadAsync(instanceId, context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                return Results.Json(layout, OverlayLayoutStore.JsonOptions);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid instance id" });
            }
        });

        app.MapPut("/layout/{instanceId}", async (string instanceId, HttpContext context) =>
        {
            if (!IsLoopback(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                Models.OverlayLayout? layout = await System.Text.Json.JsonSerializer.DeserializeAsync<Models.OverlayLayout>(
                    context.Request.Body,
                    OverlayLayoutStore.JsonOptions,
                    context.RequestAborted);
                if (layout is null)
                {
                    return Results.BadRequest(new { error = "invalid layout" });
                }

                await _layoutStore.SaveAsync(instanceId, layout, context.RequestAborted);
                OverlayRealtimeEvent evt = OverlayEventBridge.AppOverlayLayout(instanceId, layout);
                await _realtimeHub.PublishEventAsync(evt, context.RequestAborted);
                return Results.Json(layout, OverlayLayoutStore.JsonOptions);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid instance id" });
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "invalid json" });
            }
        });
    }

    private void MapExtensionRoutes(WebApplication app)
    {
        app.MapGet("/extensions", () =>
        {
            var packs = _extensionStore.ListCatalog().Select(pack => new
            {
                id = pack.Id,
                name = pack.Name,
                version = pack.Version,
                apiVersion = pack.ApiVersion,
                widgets = pack.Widgets,
                effects = pack.Effects,
                fonts = pack.Fonts,
                assets = pack.Assets,
                baseUrl = "/ext/" + pack.Id + "/"
            }).ToArray();

            return Results.Json(new { packs });
        });

        app.MapGet("/ext/{packId}/{*path}", (string packId, string? path, HttpContext context) =>
        {
            if (!_extensionStore.TryResolveFile(packId, path ?? "", out string fullPath))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Results.File(fullPath, OverlayExtensionStore.GuessContentType(fullPath));
        });

        app.MapPost("/extensions/install", async (HttpContext context) =>
        {
            if (!IsLoopback(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data mit ZIP-Datei erwartet" });
            }

            IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            IFormFile? file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "keine ZIP-Datei übermittelt" });
            }

            try
            {
                await using Stream stream = file.OpenReadStream();
                OverlayExtensionPackSummary summary = await _extensionStore.InstallFromZipAsync(stream, context.RequestAborted);
                return Results.Json(new
                {
                    id = summary.Id,
                    name = summary.Name,
                    version = summary.Version,
                    apiVersion = summary.ApiVersion,
                    widgets = summary.Widgets,
                    effects = summary.Effects,
                    fonts = summary.Fonts,
                    assets = summary.Assets
                });
            }
            catch (OverlayExtensionValidationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "ungültiges ZIP-Archiv" });
            }
        });

        app.MapDelete("/extensions/{packId}", async (string packId, HttpContext context) =>
        {
            if (!IsLoopback(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                await _extensionStore.UninstallAsync(packId, context.RequestAborted);
                return Results.Json(new { ok = true });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid pack id" });
            }
        });
    }

    private static IResult ServeCanvasPage(string assetPath, HttpContext context)
    {
        if (!CanvasOverlayAssets.TryGet(assetPath, out string html, out string contentType))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Results.Content(html, contentType);
    }

    private static bool IsLoopback(HttpContext context)
    {
        IPAddress? remote = context.Connection.RemoteIpAddress;
        return remote is null || IPAddress.IsLoopback(remote);
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

            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string text = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                await HandleIncomingClientMessageAsync(text, context.RequestAborted);
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

    private async Task HandleIncomingClientMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            string type = root.TryGetProperty("type", out JsonElement typeEl)
                ? typeEl.GetString() ?? ""
                : "";

            if (!string.Equals(type, "editor.layout.set", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "editor.layout.patch", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!root.TryGetProperty("data", out JsonElement data))
            {
                return;
            }

            string instanceId = data.TryGetProperty("instanceId", out JsonElement idEl)
                ? idEl.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            Models.OverlayLayout? layout = null;
            if (data.TryGetProperty("layout", out JsonElement layoutEl))
            {
                if (layoutEl.ValueKind == JsonValueKind.String)
                {
                    layout = System.Text.Json.JsonSerializer.Deserialize<Models.OverlayLayout>(
                        layoutEl.GetString() ?? "{}",
                        OverlayLayoutStore.JsonOptions);
                }
                else if (layoutEl.ValueKind == JsonValueKind.Object)
                {
                    layout = System.Text.Json.JsonSerializer.Deserialize<Models.OverlayLayout>(
                        layoutEl.GetRawText(),
                        OverlayLayoutStore.JsonOptions);
                }
            }

            if (layout is null)
            {
                return;
            }

            await _layoutStore.SaveAsync(instanceId, layout, cancellationToken);
            await _realtimeHub.PublishEventAsync(
                OverlayEventBridge.AppOverlayLayout(instanceId, layout),
                cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            // ignore malformed client frames
        }
        catch (ArgumentException)
        {
            // invalid instance id
        }
    }
}
