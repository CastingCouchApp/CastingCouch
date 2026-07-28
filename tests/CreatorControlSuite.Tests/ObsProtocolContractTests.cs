using System.Text.Json;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.OBS.Protocol;

namespace CreatorControlSuite.Tests;

public sealed class ObsProtocolContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    [Trait("Category", "Contract")]
    public void Hello_CreatesAuthenticatedIdentifyForRpcVersionOne()
    {
        ObsReceivedEnvelope envelope =
            ObsProtocolCodec.Decode(ReadFixture("hello-auth.json"));
        ObsHello hello = Deserialize<ObsHello>(envelope.Data);

        ObsEnvelope identify =
            ObsHandshake.CreateIdentify(hello, "contract-password");
        string json = ObsProtocolCodec.Encode(identify);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement data = root.GetProperty("d");

        Assert.Equal(0, envelope.Op);
        Assert.Equal("5.6.0", hello.ObsWebSocketVersion);
        Assert.Equal(1, root.GetProperty("op").GetInt32());
        Assert.Equal(1, data.GetProperty("rpcVersion").GetInt32());
        Assert.Equal(
            "hBX7/Dl9VT/Ag1a8AGOXSUYIpRRQmqUj/UwWwgabh/k=",
            data.GetProperty("authentication").GetString());
        Assert.Equal(
            ObsHandshake.DefaultEventSubscriptions,
            data.GetProperty("eventSubscriptions").GetInt32());
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void Identified_MapsNegotiatedRpcVersion()
    {
        ObsReceivedEnvelope envelope =
            ObsProtocolCodec.Decode(ReadFixture("identified.json"));
        ObsIdentified identified = Deserialize<ObsIdentified>(envelope.Data);

        Assert.Equal(2, envelope.Op);
        Assert.Equal(1, identified.NegotiatedRpcVersion);
    }

    [Theory]
    [InlineData("request-response-success.json", true, 100, null)]
    [InlineData(
        "request-response-failure.json",
        false,
        600,
        "No source was found by the name of `Missing`.")]
    [Trait("Category", "Contract")]
    public void RequestResponse_MapsStatusAndCorrelation(
        string fixture,
        bool result,
        int code,
        string? comment)
    {
        ObsReceivedEnvelope envelope =
            ObsProtocolCodec.Decode(ReadFixture(fixture));
        ObsRequestResponse response =
            Deserialize<ObsRequestResponse>(envelope.Data);

        Assert.Equal(7, envelope.Op);
        Assert.StartsWith("request-", response.RequestId);
        Assert.Equal(result, response.RequestStatus.Result);
        Assert.Equal(code, response.RequestStatus.Code);
        Assert.Equal(comment, response.RequestStatus.Comment);
        Assert.Equal(JsonValueKind.Object, response.ResponseData.ValueKind);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void CurrentProgramSceneEvent_RaisesMappedEvent()
    {
        ObsReceivedEnvelope envelope = ObsProtocolCodec.Decode(
            ReadFixture("event-current-program-scene.json"));
        var client = new ObsWebSocketClient();
        string? receivedScene = null;
        client.CurrentProgramSceneChanged += (_, scene) =>
            receivedScene = scene;

        client.ProcessEvent(envelope.Data);

        Assert.Equal("Contract Scene", receivedScene);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void InputVolumeMetersEvent_MapsChannelsToDecibels()
    {
        ObsReceivedEnvelope envelope = ObsProtocolCodec.Decode(
            ReadFixture("event-input-volume-meters.json"));
        var client = new ObsWebSocketClient();
        IReadOnlyList<ObsInputVolumeMeter>? receivedMeters = null;
        client.InputVolumeMeters += (_, meters) =>
            receivedMeters = meters;

        client.ProcessEvent(envelope.Data);

        ObsInputVolumeMeter meter = Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<ObsInputVolumeMeter>>(
                receivedMeters));
        Assert.Equal("Contract Mic", meter.InputName);
        Assert.Equal(0, meter.InputPeakDb, precision: 6);
        Assert.Equal(
            20 * Math.Log10(0.75),
            meter.PeakDb,
            precision: 6);
        Assert.Equal(
            20 * Math.Log10(0.5),
            meter.MagnitudeDb,
            precision: 6);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"op\":5}")]
    [InlineData("{\"op\":\"5\",\"d\":{}}")]
    [InlineData("{\"op\":5,\"d\":[]}")]
    [InlineData("not-json")]
    [Trait("Category", "Contract")]
    public void InvalidEnvelope_IsRejected(string payload)
    {
        Assert.Throws<InvalidDataException>(
            () => ObsProtocolCodec.Decode(payload));
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void OversizedEnvelope_IsRejected()
    {
        string payload = "{\"op\":5,\"d\":{\"value\":\"" +
                         new string(
                             'x',
                             ObsProtocolCodec.MaxPayloadBytes) +
                         "\"}}";

        Assert.Throws<InvalidDataException>(
            () => ObsProtocolCodec.Decode(payload));
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void Handshake_RejectsUnsupportedRpcVersion()
    {
        var hello = new ObsHello
        {
            RpcVersion = 0
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ObsHandshake.CreateIdentify(hello, null));

        Assert.Contains("RPC", exception.Message);
    }

    private static T Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(JsonOptions)
        ?? throw new InvalidOperationException(
            $"{typeof(T).Name} konnte nicht deserialisiert werden.");

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "obs",
            name));
}
