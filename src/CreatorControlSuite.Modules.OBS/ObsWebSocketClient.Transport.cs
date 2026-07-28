using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.OBS.Protocol;

namespace CreatorControlSuite.Modules.OBS;

public sealed partial class ObsWebSocketClient
{
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

        string json = ObsProtocolCodec.Encode(payload);
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
                        ProcessEvent(envelope.Data);
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

    internal void ProcessEvent(JsonElement data)
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

            if (stream.Length + result.Count >
                ObsProtocolCodec.MaxPayloadBytes)
            {
                throw new InvalidDataException(
                    "OBS-Nachricht überschreitet das Größenlimit.");
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return ObsProtocolCodec.Decode(stream.ToArray());
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

}
