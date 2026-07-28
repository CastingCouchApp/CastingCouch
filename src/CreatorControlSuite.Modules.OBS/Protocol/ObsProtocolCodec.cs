using System.Text;
using System.Text.Json;

namespace CreatorControlSuite.Modules.OBS.Protocol;

internal static class ObsProtocolCodec
{
    internal const int MaxPayloadBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static ObsReceivedEnvelope Decode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            throw new InvalidDataException(
                "OBS-Nachricht überschreitet das Größenlimit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return Decode(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Ungültige OBS-WebSocket-Nachricht.",
                exception);
        }
    }

    internal static ObsReceivedEnvelope Decode(
        ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidDataException(
                "OBS-Nachricht überschreitet das Größenlimit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return Decode(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Ungültige OBS-WebSocket-Nachricht.",
                exception);
        }
    }

    internal static string Encode(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static ObsReceivedEnvelope Decode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("op", out JsonElement opElement) ||
            opElement.ValueKind != JsonValueKind.Number ||
            !opElement.TryGetInt32(out int op) ||
            !root.TryGetProperty("d", out JsonElement dataElement) ||
            dataElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Ungültige OBS-WebSocket-Nachricht.");
        }

        return new ObsReceivedEnvelope(
            op,
            dataElement.Clone());
    }
}

internal sealed record ObsReceivedEnvelope(
    int Op,
    JsonElement Data);
