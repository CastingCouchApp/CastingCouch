using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.OBS.Protocol;

namespace CreatorControlSuite.Modules.OBS;

public sealed class ObsWebSocketClient : IObsWebSocketClient
{
    private const int HelloOp = 0;
    private const int IdentifyOp = 1;
    private const int IdentifiedOp = 2;
    private const int EventOp = 5;
    private const int RequestOp = 6;
    private const int RequestResponseOp = 7;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ObsRequestResponse>>
        _pendingRequests = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(8);

    public bool IsConnected =>
        _socket is { State: WebSocketState.Open };

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? CurrentProgramSceneChanged;
    public event EventHandler? SceneCollectionChanged;
    public event EventHandler? SceneItemsChanged;
    public event EventHandler? InputsChanged;
    public event EventHandler<IReadOnlyList<ObsInputVolumeMeter>>? InputVolumeMeters;

    public async Task ConnectAsync(
        ObsConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken);

        _requestTimeout = options.RequestTimeout;
        _socket = new ClientWebSocket();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(options.ConnectTimeout);

        var uri = new Uri($"ws://{options.Host}:{options.Port}");

        await _socket.ConnectAsync(uri, timeout.Token);

        ObsReceivedEnvelope helloEnvelope = await ReceiveEnvelopeAsync(_socket, timeout.Token);

        if (helloEnvelope.Op != HelloOp)
        {
            throw new InvalidOperationException(
                $"OBS sendete beim Verbindungsaufbau Op {helloEnvelope.Op} statt Hello.");
        }

        ObsHello hello = helloEnvelope.Data.Deserialize<ObsHello>(JsonOptions)
                    ?? throw new InvalidOperationException(
                        "OBS Hello konnte nicht gelesen werden.");

        string? authentication = null;

        if (hello.Authentication is not null)
        {
            if (string.IsNullOrWhiteSpace(options.Password))
            {
                throw new InvalidOperationException(
                    "OBS verlangt ein WebSocket-Passwort.");
            }

            authentication = ObsAuthentication.CreateResponse(
                options.Password,
                hello.Authentication.Salt,
                hello.Authentication.Challenge);
        }

        var identifyEnvelope = new ObsEnvelope
        {
            Op = IdentifyOp,
            Data = new ObsIdentify
            {
                RpcVersion = Math.Min(hello.RpcVersion, 1),
                Authentication = authentication,
                EventSubscriptions = 66031
            }
        };

        await SendJsonAsync(identifyEnvelope, timeout.Token);

        ObsReceivedEnvelope identifiedEnvelope = await ReceiveEnvelopeAsync(_socket, timeout.Token);

        if (identifiedEnvelope.Op != IdentifiedOp)
        {
            throw new InvalidOperationException(
                $"OBS-Authentifizierung fehlgeschlagen. Empfangener Op: {identifiedEnvelope.Op}.");
        }

        _ = identifiedEnvelope.Data.Deserialize<ObsIdentified>(JsonOptions)
            ?? throw new InvalidOperationException(
                "OBS Identified konnte nicht gelesen werden.");

        _receiveCancellation = new CancellationTokenSource();
        _receiveTask = Task.Run(
            () => ReceiveLoopAsync(_receiveCancellation.Token),
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

        foreach (TaskCompletionSource<ObsRequestResponse> pending in _pendingRequests.Values)
        {
            pending.TrySetException(
                new InvalidOperationException("OBS-Verbindung wurde getrennt."));
        }

        _pendingRequests.Clear();
        _receiveCancellation?.Dispose();
        _receiveCancellation = null;
        _receiveTask = null;
        _socket?.Dispose();
        _socket = null;

        ConnectionStateChanged?.Invoke(this, false);
    }

    public async Task<ObsServerInfo> GetVersionAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetVersion",
            requestData: null,
            cancellationToken);

        return new ObsServerInfo(
            GetString(data, "obsVersion"),
            GetString(data, "obsWebSocketVersion"),
            GetInt32(data, "rpcVersion"),
            GetString(data, "platform"),
            GetString(data, "platformDescription"));
    }

    public async Task<IReadOnlyList<ObsSceneInfo>> GetSceneListAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetSceneList",
            requestData: null,
            cancellationToken);

        var scenes = new List<ObsSceneInfo>();

        if (data.TryGetProperty("scenes", out JsonElement sceneArray))
        {
            foreach (JsonElement scene in sceneArray.EnumerateArray())
            {
                scenes.Add(new ObsSceneInfo(
                    GetString(scene, "sceneName"),
                    GetInt32(scene, "sceneIndex")));
            }
        }

        return [.. scenes.OrderBy(scene => scene.Index)];
    }

    public async Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetInputList",
            requestData: null,
            cancellationToken);

        var inputs = new List<ObsInputInfo>();

        if (data.TryGetProperty("inputs", out JsonElement inputArray))
        {
            foreach (JsonElement input in inputArray.EnumerateArray())
            {
                inputs.Add(new ObsInputInfo(
                    GetString(input, "inputName"),
                    GetString(input, "inputKind"),
                    GetString(input, "unversionedInputKind")));
            }
        }

        return [.. inputs.OrderBy(input => input.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<ObsTransitionInfo>> GetSceneTransitionListAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetSceneTransitionList",
            requestData: null,
            cancellationToken);

        var transitions = new List<ObsTransitionInfo>();
        if (data.TryGetProperty("transitions", out JsonElement transitionArray))
        {
            foreach (JsonElement transition in transitionArray.EnumerateArray())
            {
                transitions.Add(new ObsTransitionInfo(
                    GetString(transition, "transitionName"),
                    GetString(transition, "transitionKind"),
                    GetBoolean(transition, "transitionConfigurable")));
            }
        }

        return [.. transitions
            .Where(transition => !string.IsNullOrWhiteSpace(transition.Name))
            .OrderBy(transition => transition.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        JsonElement data = await SendRequestAsync("GetSceneItemList", new { sceneName }, cancellationToken);
        var items = new List<ObsSceneItemInfo>();
        if (data.TryGetProperty("sceneItems", out JsonElement itemArray))
        {
            foreach (JsonElement item in itemArray.EnumerateArray())
            {
                items.Add(new ObsSceneItemInfo(
                    GetInt32(item, "sceneItemId"), GetInt32(item, "sceneItemIndex"),
                    GetString(item, "sourceName"), GetString(item, "sourceType"),
                    GetBoolean(item, "sceneItemEnabled"), GetBoolean(item, "sceneItemLocked"),
                    GetBoolean(item, "isGroup")));
            }
        }
        return [.. items.OrderByDescending(item => item.Index).ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<ObsInputAudioState> GetInputAudioStateAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        JsonElement mute = await SendRequestAsync("GetInputMute", new { inputName }, cancellationToken);
        JsonElement volume = await SendRequestAsync("GetInputVolume", new { inputName }, cancellationToken);
        return new ObsInputAudioState(
            inputName,
            GetBoolean(mute, "inputMuted"),
            volume.TryGetProperty("inputVolumeDb", out JsonElement db) ? db.GetDouble() : 0d);
    }

    public Task SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetInputMute", new { inputName, inputMuted = muted }, cancellationToken);

    public Task SetInputVolumeDbAsync(string inputName, double volumeDb, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetInputVolume", new { inputName, inputVolumeDb = volumeDb }, cancellationToken);

    public async Task<ObsInputAdvancedAudioState> GetInputAdvancedAudioStateAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        JsonElement monitor = await SendRequestAsync("GetInputAudioMonitorType", new { inputName }, cancellationToken);
        JsonElement sync = await SendRequestAsync("GetInputAudioSyncOffset", new { inputName }, cancellationToken);
        return new ObsInputAdvancedAudioState(
            inputName,
            GetString(monitor, "monitorType"),
            GetInt32(sync, "inputAudioSyncOffset"));
    }

    public Task SetInputAudioMonitorTypeAsync(string inputName, string monitorType, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetInputAudioMonitorType", new { inputName, monitorType }, cancellationToken);

    public Task SetInputAudioSyncOffsetAsync(string inputName, int syncOffsetMilliseconds, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetInputAudioSyncOffset", new { inputName, inputAudioSyncOffset = syncOffsetMilliseconds }, cancellationToken);

    public async Task<string> GetCurrentProgramSceneAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetCurrentProgramScene",
            requestData: null,
            cancellationToken);

        return GetString(data, "currentProgramSceneName");
    }

    public async Task SetCurrentProgramSceneAsync(
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);

        await SendRequestAsync(
            "SetCurrentProgramScene",
            new { sceneName },
            cancellationToken);
    }

    public async Task SetCurrentSceneTransitionAsync(
        string transitionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transitionName);
        await SendRequestAsync(
            "SetCurrentSceneTransition",
            new { transitionName },
            cancellationToken);
    }

    public async Task SetCurrentSceneTransitionDurationAsync(
        int transitionDurationMilliseconds,
        CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(
            "SetCurrentSceneTransitionDuration",
            new { transitionDuration = Math.Clamp(transitionDurationMilliseconds, 50, 20000) },
            cancellationToken);
    }

    public async Task<ObsStreamStatus> GetStreamStatusAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetStreamStatus",
            requestData: null,
            cancellationToken);

        return new ObsStreamStatus(
            GetBoolean(data, "outputActive"),
            GetBoolean(data, "outputReconnecting"),
            GetString(data, "outputTimecode"),
            GetInt64(data, "outputDuration"),
            GetInt64(data, "outputBytes"),
            GetInt32(data, "outputSkippedFrames"),
            GetInt32(data, "outputTotalFrames"));
    }

    public async Task<ObsStats> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetStats",
            requestData: null,
            cancellationToken);

        return new ObsStats(
            GetDouble(data, "cpuUsage"),
            GetDouble(data, "memoryUsage"),
            GetDouble(data, "availableDiskSpace"),
            GetDouble(data, "activeFps"),
            GetDouble(data, "averageFrameRenderTime"),
            GetInt32(data, "renderSkippedFrames"),
            GetInt32(data, "renderTotalFrames"),
            GetInt32(data, "outputSkippedFrames"),
            GetInt32(data, "outputTotalFrames"));
    }

    public Task StartStreamAsync(
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "StartStream",
            requestData: null,
            cancellationToken);
    }

    public Task StopStreamAsync(
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "StopStream",
            requestData: null,
            cancellationToken);
    }

    public async Task<ObsOutputStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetRecordStatus", null, cancellationToken);
        return new ObsOutputStatus(GetBoolean(data, "outputActive"), GetBoolean(data, "outputPaused"),
            GetString(data, "outputTimecode"), GetInt64(data, "outputDuration"), GetInt64(data, "outputBytes"));
    }

    public Task StartRecordAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StartRecord", null, cancellationToken);
    public Task StopRecordAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StopRecord", null, cancellationToken);
    public Task PauseRecordAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("PauseRecord", null, cancellationToken);
    public Task ResumeRecordAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("ResumeRecord", null, cancellationToken);

    public async Task<ObsOutputStatus> GetReplayBufferStatusAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetReplayBufferStatus", null, cancellationToken);
        return new ObsOutputStatus(GetBoolean(data, "outputActive"), false, string.Empty, 0, 0);
    }

    public Task StartReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StartReplayBuffer", null, cancellationToken);
    public Task StopReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StopReplayBuffer", null, cancellationToken);
    public Task SaveReplayBufferAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("SaveReplayBuffer", null, cancellationToken);

    public async Task<bool> GetVirtualCamStatusAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetVirtualCamStatus", null, cancellationToken);
        return GetBoolean(data, "outputActive");
    }

    public Task StartVirtualCamAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StartVirtualCam", null, cancellationToken);
    public Task StopVirtualCamAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithoutResultAsync("StopVirtualCam", null, cancellationToken);

    public async Task<bool> InputExistsAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await SendRequestAsync(
                "GetInputSettings",
                new { inputName },
                cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }


    public async Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> GetInputSettingsAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetInputSettings",
            new { inputName },
            cancellationToken);
        var result = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (data.TryGetProperty("inputSettings", out JsonElement settings) && settings.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (JsonProperty property in settings.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }
        }
        return result;
    }

    public async Task<bool> SceneItemExistsAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await SendRequestAsync(
                "GetSceneItemId",
                new
                {
                    sceneName,
                    sourceName
                },
                cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureSceneAsync(
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ObsSceneInfo> scenes = await GetSceneListAsync(cancellationToken);

        if (scenes.Any(scene =>
                string.Equals(
                    scene.Name,
                    sceneName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await SendRequestAsync(
            "CreateScene",
            new { sceneName },
            cancellationToken);
    }

    public Task CreateInputAsync(
        string sceneName,
        string inputName,
        string inputKind,
        object inputSettings,
        bool sceneItemEnabled,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "CreateInput",
            new { sceneName, inputName, inputKind, inputSettings, sceneItemEnabled },
            cancellationToken);
    }

    public Task CreateSceneItemAsync(
        string sceneName,
        string sourceName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "CreateSceneItem",
            new { sceneName, sourceName, sceneItemEnabled = enabled },
            cancellationToken);
    }

    public async Task EnsureMediaInputAsync(
        string sceneName,
        string inputName,
        string localFile,
        CancellationToken cancellationToken = default)
    {
        await EnsureSceneAsync(sceneName, cancellationToken);

        var settings = new
        {
            local_file = localFile,
            looping = false,
            restart_on_activate = false,
            close_when_inactive = true,
            clear_on_media_end = true,
            speed_percent = 100
        };

        if (!await InputExistsAsync(inputName, cancellationToken))
        {
            await SendRequestAsync(
                "CreateInput",
                new
                {
                    sceneName,
                    inputName,
                    inputKind = "ffmpeg_source",
                    inputSettings = settings,
                    sceneItemEnabled = false
                },
                cancellationToken);

            return;
        }

        await SetInputSettingsAsync(
            inputName,
            settings,
            overlay: false,
            cancellationToken);

        if (!await SceneItemExistsAsync(
                sceneName,
                inputName,
                cancellationToken))
        {
            await SendRequestAsync(
                "CreateSceneItem",
                new
                {
                    sceneName,
                    sourceName = inputName,
                    sceneItemEnabled = false
                },
                cancellationToken);
        }
    }

    public async Task EnsureTextInputAsync(
        string sceneName,
        string inputName,
        string text,
        string fontFace,
        int fontSize,
        string fontColor,
        CancellationToken cancellationToken = default)
    {
        await EnsureSceneAsync(sceneName, cancellationToken);

        var settings = new
        {
            text,
            color = ParseObsColor(fontColor),
            font = new
            {
                face = fontFace,
                size = fontSize,
                style = "Regular",
                flags = 0
            },
            outline = true,
            outline_size = 2,
            outline_color = 0,
            outline_opacity = 100,
            align = "center",
            valign = "center"
        };

        if (!await InputExistsAsync(inputName, cancellationToken))
        {
            await SendRequestAsync(
                "CreateInput",
                new
                {
                    sceneName,
                    inputName,
                    inputKind = "text_gdiplus_v3",
                    inputSettings = settings,
                    sceneItemEnabled = false
                },
                cancellationToken);

            return;
        }

        await SetInputSettingsAsync(
            inputName,
            settings,
            overlay: true,
            cancellationToken);

        if (!await SceneItemExistsAsync(
                sceneName,
                inputName,
                cancellationToken))
        {
            await SendRequestAsync(
                "CreateSceneItem",
                new
                {
                    sceneName,
                    sourceName = inputName,
                    sceneItemEnabled = false
                },
                cancellationToken);
        }
    }

    public async Task SetInputSettingsAsync(
        string inputName,
        object inputSettings,
        bool overlay,
        CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(
            "SetInputSettings",
            new
            {
                inputName,
                inputSettings,
                overlay
            },
            cancellationToken);
    }

    public Task RestartMediaInputAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "TriggerMediaInputAction",
            new
            {
                inputName,
                mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
            },
            cancellationToken);
    }

    public Task StopMediaInputAsync(
        string inputName,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "TriggerMediaInputAction",
            new
            {
                inputName,
                mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"
            },
            cancellationToken);
    }

    public Task PressInputPropertiesButtonAsync(
        string inputName,
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "PressInputPropertiesButton",
            new
            {
                inputName,
                propertyName
            },
            cancellationToken);
    }

    public async Task SetSceneItemEnabledAsync(
        string sceneName,
        string sourceName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(
            sceneName,
            sourceName,
            cancellationToken);

        await SendRequestAsync(
            "SetSceneItemEnabled",
            new
            {
                sceneName,
                sceneItemId = itemId,
                sceneItemEnabled = enabled
            },
            cancellationToken);
    }

    public async Task SetSceneItemLockedAsync(
        string sceneName,
        string sourceName,
        bool locked,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
        await SendRequestAsync(
            "SetSceneItemLocked",
            new { sceneName, sceneItemId = itemId, sceneItemLocked = locked },
            cancellationToken);
    }

    public async Task SetSceneItemIndexAsync(
        string sceneName,
        string sourceName,
        int sceneItemIndex,
        CancellationToken cancellationToken = default)
    {
        if (sceneItemIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneItemIndex));
        }

        int itemId = await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
        await SendRequestAsync(
            "SetSceneItemIndex",
            new { sceneName, sceneItemId = itemId, sceneItemIndex },
            cancellationToken);
    }

    public async Task SetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        double x,
        double y,
        double width,
        double height,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(
            sceneName,
            sourceName,
            cancellationToken);

        await SendRequestAsync(
            "SetSceneItemTransform",
            new
            {
                sceneName,
                sceneItemId = itemId,
                sceneItemTransform = new
                {
                    positionX = x,
                    positionY = y,
                    boundsType = "OBS_BOUNDS_SCALE_INNER",
                    boundsWidth = width,
                    boundsHeight = height,
                    boundsAlignment = 0,
                    cropToBounds = true
                }
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ObsSourceFilterInfo>> GetSourceFilterListAsync(
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetSourceFilterList",
            new { sourceName },
            cancellationToken);

        var filters = new List<ObsSourceFilterInfo>();
        if (data.TryGetProperty("filters", out JsonElement filterArray))
        {
            foreach (JsonElement filter in filterArray.EnumerateArray())
            {
                filters.Add(new ObsSourceFilterInfo(
                    GetString(filter, "filterName"),
                    GetString(filter, "filterKind"),
                    filter.TryGetProperty("filterEnabled", out JsonElement enabledElement) && enabledElement.GetBoolean(),
                    GetInt32(filter, "filterIndex")));
            }
        }

        return [.. filters.OrderByDescending(filter => filter.Index)];
    }

    public Task SetSourceFilterEnabledAsync(
        string sourceName,
        string filterName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return SendRequestWithoutResultAsync(
            "SetSourceFilterEnabled",
            new { sourceName, filterName, filterEnabled = enabled },
            cancellationToken);
    }

    public async Task<ObsSceneItemTransformInfo> GetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
        JsonElement data = await SendRequestAsync(
            "GetSceneItemTransform",
            new { sceneName, sceneItemId = itemId },
            cancellationToken);

        JsonElement transform = data.TryGetProperty("sceneItemTransform", out JsonElement value) ? value : data;
        static double ReadDouble(JsonElement element, string name, double fallback = 0)
            => element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.Number
                ? property.GetDouble()
                : fallback;
        static int ReadInt(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : 0;

        double sourceWidth = ReadDouble(transform, "sourceWidth", 1);
        double sourceHeight = ReadDouble(transform, "sourceHeight", 1);
        double width = ReadDouble(transform, "width", sourceWidth);
        double height = ReadDouble(transform, "height", sourceHeight);

        return new ObsSceneItemTransformInfo(
            ReadDouble(transform, "positionX"),
            ReadDouble(transform, "positionY"),
            width,
            height,
            ReadDouble(transform, "rotation"),
            ReadInt(transform, "cropLeft"),
            ReadInt(transform, "cropTop"),
            ReadInt(transform, "cropRight"),
            ReadInt(transform, "cropBottom"));
    }

    public async Task ResetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
        await SendRequestAsync(
            "ResetSceneItemTransform",
            new { sceneName, sceneItemId = itemId },
            cancellationToken);
    }

    public async Task SetSceneItemDetailedTransformAsync(
        string sceneName,
        string sourceName,
        double x,
        double y,
        double width,
        double height,
        double rotation,
        int cropLeft,
        int cropTop,
        int cropRight,
        int cropBottom,
        CancellationToken cancellationToken = default)
    {
        int itemId = await GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
        await SendRequestAsync(
            "SetSceneItemTransform",
            new
            {
                sceneName,
                sceneItemId = itemId,
                sceneItemTransform = new
                {
                    positionX = x,
                    positionY = y,
                    rotation,
                    boundsType = "OBS_BOUNDS_SCALE_INNER",
                    boundsWidth = width,
                    boundsHeight = height,
                    boundsAlignment = 0,
                    cropToBounds = true,
                    cropLeft,
                    cropTop,
                    cropRight,
                    cropBottom
                }
            },
            cancellationToken);
    }

    private async Task<int> GetSceneItemIdAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken)
    {
        JsonElement data = await SendRequestAsync(
            "GetSceneItemId",
            new
            {
                sceneName,
                sourceName
            },
            cancellationToken);

        return GetInt32(data, "sceneItemId");
    }

    private static int ParseObsColor(string htmlColor)
    {
        string value = htmlColor.Trim().TrimStart('#');

        if (value.Length != 6)
        {
            return 0xFFFFFF;
        }

        int red = Convert.ToInt32(value[..2], 16);
        int green = Convert.ToInt32(value.Substring(2, 2), 16);
        int blue = Convert.ToInt32(value.Substring(4, 2), 16);

        return (blue << 16) | (green << 8) | red;
    }

    public async Task<(string CurrentProfile, IReadOnlyList<string> Profiles)> GetProfileListAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetProfileList", null, cancellationToken);
        string current = data.TryGetProperty("currentProfileName", out JsonElement currentElement) ? currentElement.GetString() ?? "" : "";
        string[] profiles = data.TryGetProperty("profiles", out JsonElement listElement)
            ? [.. listElement.EnumerateArray().Select(x => x.TryGetProperty("profileName", out JsonElement n) ? n.GetString() ?? "" : "").Where(x => x.Length > 0)]
            : [];
        return (current, profiles);
    }

    public Task SetCurrentProfileAsync(string profileName, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetCurrentProfile", new { profileName }, cancellationToken);

    public async Task<(string CurrentSceneCollection, IReadOnlyList<string> SceneCollections)> GetSceneCollectionListAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetSceneCollectionList", null, cancellationToken);
        string current = data.TryGetProperty("currentSceneCollectionName", out JsonElement currentElement) ? currentElement.GetString() ?? "" : "";
        string[] collections = data.TryGetProperty("sceneCollections", out JsonElement listElement)
            ? [.. listElement.EnumerateArray().Select(x => x.TryGetProperty("sceneCollectionName", out JsonElement n) ? n.GetString() ?? "" : "").Where(x => x.Length > 0)]
            : [];
        return (current, collections);
    }

    public Task SetCurrentSceneCollectionAsync(string sceneCollectionName, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetCurrentSceneCollection", new { sceneCollectionName }, cancellationToken);

    public async Task<byte[]> GetSourceScreenshotAsync(
        string sourceName,
        int imageWidth = 640,
        int? imageHeight = 360,
        CancellationToken cancellationToken = default)
    {
        object requestData = imageHeight is int height
            ? new
            {
                sourceName,
                imageFormat = "png",
                imageWidth,
                imageHeight = height,
                imageCompressionQuality = -1
            }
            : new
            {
                sourceName,
                imageFormat = "png",
                imageWidth,
                imageCompressionQuality = -1
            };

        JsonElement data = await SendRequestAsync(
            "GetSourceScreenshot",
            requestData,
            cancellationToken);

        if (!data.TryGetProperty("imageData", out JsonElement imageDataElement))
        {
            return [];
        }

        string? imageData = imageDataElement.GetString();
        if (string.IsNullOrWhiteSpace(imageData))
        {
            return [];
        }

        int commaIndex = imageData.IndexOf(',');
        string base64 = commaIndex >= 0 ? imageData[(commaIndex + 1)..] : imageData;
        return Convert.FromBase64String(base64);
    }

    public async Task<ObsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        Task<ObsServerInfo> versionTask = GetVersionAsync(cancellationToken);
        Task<IReadOnlyList<ObsSceneInfo>> scenesTask = GetSceneListAsync(cancellationToken);
        Task<IReadOnlyList<ObsInputInfo>> inputsTask = GetInputListAsync(cancellationToken);
        Task<string> currentSceneTask = GetCurrentProgramSceneAsync(cancellationToken);
        Task<ObsStreamStatus> streamTask = GetStreamStatusAsync(cancellationToken);

        await Task.WhenAll(
            versionTask,
            scenesTask,
            inputsTask,
            currentSceneTask,
            streamTask);

        return new ObsSnapshot(
            Connected: IsConnected,
            CurrentProgramScene: await currentSceneTask,
            CurrentPreviewScene: "",
            Scenes: await scenesTask,
            Inputs: await inputsTask,
            Server: await versionTask,
            Stream: await streamTask);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }

    private async Task SendRequestWithoutResultAsync(
        string requestType,
        object? requestData,
        CancellationToken cancellationToken)
    {
        _ = await SendRequestAsync(
            requestType,
            requestData,
            cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(
        string requestType,
        object? requestData,
        CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "OBS ist nicht verbunden.");
        }

        string requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ObsRequestResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException(
                "OBS Request-ID konnte nicht registriert werden.");
        }

        var envelope = new ObsEnvelope
        {
            Op = RequestOp,
            Data = new ObsRequestPayload
            {
                RequestType = requestType,
                RequestId = requestId,
                RequestData = requestData
            }
        };

        try
        {
            await SendJsonAsync(envelope, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

            timeout.CancelAfter(_requestTimeout);

            ObsRequestResponse response = await completion.Task.WaitAsync(timeout.Token);

            if (!response.RequestStatus.Result)
            {
                throw new InvalidOperationException(
                    $"OBS Request {requestType} fehlgeschlagen " +
                    $"({response.RequestStatus.Code}): " +
                    response.RequestStatus.Comment);
            }

            return response.ResponseData;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task SendJsonAsync(
        object payload,
        CancellationToken cancellationToken)
    {
        ClientWebSocket socket = _socket
                     ?? throw new InvalidOperationException(
                         "OBS WebSocket ist nicht initialisiert.");

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await socket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        CancellationToken cancellationToken)
    {
        ClientWebSocket socket = _socket
                     ?? throw new InvalidOperationException(
                         "OBS WebSocket ist nicht initialisiert.");

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State == WebSocketState.Open)
            {
                ObsReceivedEnvelope envelope = await ReceiveEnvelopeAsync(
                    socket,
                    cancellationToken);

                switch (envelope.Op)
                {
                    case RequestResponseOp:
                        {
                            ObsRequestResponse? response =
                                envelope.Data.Deserialize<ObsRequestResponse>(
                                    JsonOptions);

                            if (response is not null &&
                                _pendingRequests.TryGetValue(
                                    response.RequestId,
                                    out TaskCompletionSource<ObsRequestResponse>? completion))
                            {
                                completion.TrySetResult(response);
                            }

                            break;
                        }

                    case EventOp:
                        HandleEvent(envelope.Data);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            foreach (TaskCompletionSource<ObsRequestResponse> pending in _pendingRequests.Values)
            {
                pending.TrySetException(exception);
            }
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    private void HandleEvent(JsonElement data)
    {
        if (!data.TryGetProperty("eventType", out JsonElement eventTypeElement) ||
            !data.TryGetProperty("eventData", out JsonElement eventData))
        {
            return;
        }

        string? eventType = eventTypeElement.GetString();

        if (eventType is "SceneCreated" or "SceneRemoved" or "SceneNameChanged" or "SceneListChanged")
        {
            SceneCollectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (eventType is "SceneItemCreated" or "SceneItemRemoved" or "SceneItemEnableStateChanged" or "SceneItemLockStateChanged" or "SceneItemListReindexed" or "SceneItemTransformChanged")
        {
            SceneItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (eventType is "InputCreated" or "InputRemoved" or "InputNameChanged" or "InputMuteStateChanged" or "InputVolumeChanged" or "InputActiveStateChanged")
        {
            InputsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (string.Equals(eventType, "InputVolumeMeters", StringComparison.Ordinal) &&
            eventData.TryGetProperty("inputs", out JsonElement meterInputs) &&
            meterInputs.ValueKind == JsonValueKind.Array)
        {
            var meters = new List<ObsInputVolumeMeter>();
            foreach (JsonElement meterInput in meterInputs.EnumerateArray())
            {
                string inputName = meterInput.TryGetProperty("inputName", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(inputName) || !meterInput.TryGetProperty("inputLevelsMul", out JsonElement levels) || levels.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                double magnitude = 0, peak = 0, inputPeak = 0;
                int channelCount = 0;
                foreach (JsonElement channel in levels.EnumerateArray())
                {
                    if (channel.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    double[] values = [.. channel.EnumerateArray().Select(value => value.GetDouble())];
                    if (values.Length < 3)
                    {
                        continue;
                    }

                    magnitude = Math.Max(magnitude, values[0]);
                    peak = Math.Max(peak, values[1]);
                    inputPeak = Math.Max(inputPeak, values[2]);
                    channelCount++;
                }
                if (channelCount == 0)
                {
                    continue;
                }

                static double MulToDb(double value) => value <= 0.000001 ? -60 : Math.Clamp(20 * Math.Log10(value), -60, 10);
                meters.Add(new ObsInputVolumeMeter(inputName, MulToDb(magnitude), MulToDb(peak), MulToDb(inputPeak)));
            }
            if (meters.Count > 0)
            {
                InputVolumeMeters?.Invoke(this, meters);
            }
        }

        if (string.Equals(
                eventType,
                "CurrentProgramSceneChanged",
                StringComparison.Ordinal) &&
            eventData.TryGetProperty(
                "sceneName",
                out JsonElement sceneNameElement))
        {
            string? sceneName = sceneNameElement.GetString();

            if (!string.IsNullOrWhiteSpace(sceneName))
            {
                CurrentProgramSceneChanged?.Invoke(
                    this,
                    sceneName);
            }
        }
    }

    private static async Task<ObsReceivedEnvelope> ReceiveEnvelopeAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(
                buffer,
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    "OBS hat die WebSocket-Verbindung geschlossen.");
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }

            if (stream.Length > 4 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "OBS-Nachricht überschreitet das Größenlimit.");
            }
        }

        stream.Position = 0;

        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("op", out JsonElement opElement) ||
            !root.TryGetProperty("d", out JsonElement dataElement))
        {
            throw new InvalidOperationException(
                "Ungültige OBS-WebSocket-Nachricht.");
        }

        return new ObsReceivedEnvelope(
            opElement.GetInt32(),
            dataElement.Clone());
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            ? property.GetString() ?? ""
            : "";
    }

    private static int GetInt32(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.TryGetInt32(out int value)
            ? value
            : 0;
    }

    private static long GetInt64(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.TryGetInt64(out long value)
            ? value
            : 0;
    }

    private static double GetDouble(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.TryGetDouble(out double value)
            ? value
            : 0d;
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False && property.GetBoolean();
    }

    private sealed record ObsReceivedEnvelope(
        int Op,
        JsonElement Data);
}
