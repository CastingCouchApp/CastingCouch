using System.Net;
using System.Text.Json;

namespace CreatorControlSuite.Modules.Twitch;

/// <summary>
/// Maps Helix raid HTTP failures to short German UI messages.
/// </summary>
public static class TwitchRaidErrorMapper
{
    public static string FormatStartRaidError(HttpStatusCode statusCode, string responseBody)
    {
        string detail = ExtractMessage(responseBody);
        return ((int)statusCode) switch
        {
            400 => string.IsNullOrWhiteSpace(detail)
                ? "Twitch hat den Raid abgelehnt (ungültige Anfrage)."
                : $"Twitch hat den Raid abgelehnt: {detail}",
            401 => "Twitch-Anmeldung abgelaufen. Bitte Twitch erneut verbinden.",
            403 => "Keine Berechtigung für Raids (Scope channel:manage:raids fehlt oder ist ungültig).",
            404 => "Raid-Ziel wurde auf Twitch nicht gefunden.",
            409 => string.IsNullOrWhiteSpace(detail)
                ? "Raid derzeit nicht möglich (Cooldown, bereits aktiv oder Ziel offline)."
                : $"Raid derzeit nicht möglich: {detail}",
            429 => "Twitch Rate-Limit erreicht. Raid wird erneut versucht.",
            500 or 502 or 503 or 504 => "Twitch ist vorübergehend nicht erreichbar. Raid wird erneut versucht.",
            _ => string.IsNullOrWhiteSpace(detail)
                ? $"Twitch API {(int)statusCode}: Raid fehlgeschlagen."
                : $"Twitch API {(int)statusCode}: {detail}"
        };
    }

    public static string FormatCancelRaidError(HttpStatusCode statusCode, string responseBody)
    {
        string detail = ExtractMessage(responseBody);
        return ((int)statusCode) switch
        {
            404 => "Kein aktiver Raid zum Abbrechen.",
            429 => "Twitch Rate-Limit beim Abbrechen des Raids.",
            500 or 502 or 503 or 504 => "Twitch ist vorübergehend nicht erreichbar.",
            _ => string.IsNullOrWhiteSpace(detail)
                ? $"Twitch API {(int)statusCode}: Raid-Abbruch fehlgeschlagen."
                : $"Twitch API {(int)statusCode}: {detail}"
        };
    }

    private static string ExtractMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString()?.Trim() ?? "";
            }
        }
        catch (JsonException)
        {
            // Fall through to raw body snippet.
        }

        string trimmed = responseBody.Trim();
        return trimmed.Length <= 180 ? trimmed : trimmed[..180] + "…";
    }
}
