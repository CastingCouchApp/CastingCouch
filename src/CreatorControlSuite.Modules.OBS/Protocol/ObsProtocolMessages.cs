using System.Text.Json.Serialization;

namespace CreatorControlSuite.Modules.OBS.Protocol;

internal sealed class ObsEnvelope
{
    [JsonPropertyName("op")]
    public int Op { get; set; }

    [JsonPropertyName("d")]
    public object? Data { get; set; }
}

internal sealed class ObsHello
{
    [JsonPropertyName("obsWebSocketVersion")]
    public string ObsWebSocketVersion { get; set; } = "";

    [JsonPropertyName("rpcVersion")]
    public int RpcVersion { get; set; }

    [JsonPropertyName("authentication")]
    public ObsAuthenticationChallenge? Authentication { get; set; }
}

internal sealed class ObsAuthenticationChallenge
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = "";

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = "";
}

internal sealed class ObsIdentify
{
    [JsonPropertyName("rpcVersion")]
    public int RpcVersion { get; set; }

    [JsonPropertyName("authentication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Authentication { get; set; }

    [JsonPropertyName("eventSubscriptions")]
    public int EventSubscriptions { get; set; }
}

internal sealed class ObsIdentified
{
    [JsonPropertyName("negotiatedRpcVersion")]
    public int NegotiatedRpcVersion { get; set; }
}

internal sealed class ObsRequestPayload
{
    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("requestData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? RequestData { get; set; }
}

internal sealed class ObsRequestResponse
{
    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "";

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("requestStatus")]
    public ObsRequestStatus RequestStatus { get; set; } = new();

    [JsonPropertyName("responseData")]
    public System.Text.Json.JsonElement ResponseData { get; set; }
}

internal sealed class ObsRequestStatus
{
    [JsonPropertyName("result")]
    public bool Result { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}
