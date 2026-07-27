using System.Text.Json;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Modules.Overlay;

public sealed record OverlayChatMessagePart(
    string Type,
    string Text,
    string? Url = null,
    string? Provider = null);

public sealed record OverlayChatBadgePart(
    string SetId,
    string Id,
    string? Url = null,
    string? Title = null);

public static class OverlayEventBridge
{
    private static readonly JsonSerializerOptions PartsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static OverlayRealtimeEvent FromTwitch(
        string type,
        string summary,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string>? data) =>
        new(
            Source: "twitch",
            Type: type ?? "",
            At: receivedAt,
            Summary: summary ?? "",
            Data: data ?? new Dictionary<string, string>());

    public static OverlayRealtimeEvent FromChatMessage(
        string messageId,
        string userName,
        string userLogin,
        string color,
        IReadOnlyList<OverlayChatBadgePart> badges,
        string summary,
        DateTimeOffset at,
        IReadOnlyList<OverlayChatMessagePart> parts)
    {
        string partsJson = JsonSerializer.Serialize(parts ?? [], PartsJsonOptions);
        string badgesJson = JsonSerializer.Serialize(badges ?? [], PartsJsonOptions);
        return new OverlayRealtimeEvent(
            Source: "twitch",
            Type: "channel.chat.message",
            At: at,
            Summary: summary ?? "",
            Data: new Dictionary<string, string>
            {
                ["messageId"] = messageId ?? "",
                ["userName"] = userName ?? "",
                ["userLogin"] = userLogin ?? "",
                ["color"] = color ?? "",
                ["badges"] = badgesJson,
                ["parts"] = partsJson
            });
    }

    public static OverlayRealtimeEvent AppStreamPhase(string phase) =>
        App(
            "app.stream.phase",
            $"Phase: {phase}",
            new Dictionary<string, string> { ["phase"] = phase ?? "" });

    public static OverlayRealtimeEvent AppCountdown(
        bool isRunning,
        int remainingSeconds,
        int totalSeconds,
        string label,
        DateTimeOffset? endsAt = null) =>
        App(
            "app.countdown",
            isRunning
                ? $"Countdown: {Math.Max(0, remainingSeconds)}s"
                : "Countdown gestoppt",
            new Dictionary<string, string>
            {
                ["isRunning"] = isRunning ? "true" : "false",
                ["remainingSeconds"] = Math.Max(0, remainingSeconds).ToString(),
                ["totalSeconds"] = Math.Max(0, totalSeconds).ToString(),
                ["label"] = label ?? "",
                ["endsAt"] = endsAt?.ToString("O") ?? ""
            });

    public static OverlayRealtimeEvent AppStreamLive(bool isLive) =>
        App(
            "app.stream.live",
            isLive ? "Stream live" : "Stream offline",
            new Dictionary<string, string> { ["isLive"] = isLive ? "true" : "false" });

    public static OverlayRealtimeEvent AppObsScene(string scene) =>
        App(
            "app.obs.scene",
            $"Szene: {scene}",
            new Dictionary<string, string> { ["scene"] = scene ?? "" });

    public static OverlayRealtimeEvent AppSpotifyTrack(
        string title,
        string artist,
        string coverUrl) =>
        AppMusicTrack(MusicProviderIds.Spotify, title, artist, coverUrl);

    public static OverlayRealtimeEvent AppMusicTrack(
        string providerId,
        string title,
        string artist,
        string coverUrl)
    {
        string provider = MusicProviderIds.Normalize(providerId);
        string label = MusicProviderIds.DisplayName(provider);
        return App(
            "app.music.track",
            string.IsNullOrWhiteSpace(artist) ? title : $"{artist} – {title}",
            new Dictionary<string, string>
            {
                ["provider"] = provider,
                ["providerDisplayName"] = label,
                ["title"] = title ?? "",
                ["artist"] = artist ?? "",
                ["coverUrl"] = coverUrl ?? ""
            });
    }

    public static OverlayRealtimeEvent AppAlert(string alertType, string user) =>
        App(
            "app.alert",
            string.IsNullOrWhiteSpace(user) ? alertType : $"{alertType}: {user}",
            new Dictionary<string, string>
            {
                ["alertType"] = alertType ?? "",
                ["user"] = user ?? ""
            });

    public static OverlayRealtimeEvent AppWsHello(
        int clients,
        IReadOnlyList<(string Id, string Name)> overlays)
    {
        var data = new Dictionary<string, string>
        {
            ["clients"] = clients.ToString()
        };
        for (int i = 0; i < overlays.Count; i++)
        {
            data[$"overlay.{i}.id"] = overlays[i].Id;
            data[$"overlay.{i}.name"] = overlays[i].Name;
        }

        return App("app.ws.hello", "connected", data);
    }

    public static OverlayRealtimeEvent AppOverlayLayout(
        string instanceId,
        Models.OverlayLayout layout)
    {
        string layoutJson = JsonSerializer.Serialize(layout, OverlayLayoutStore.JsonOptions);
        return App(
            "app.overlay.layout",
            $"Layout: {instanceId}",
            new Dictionary<string, string>
            {
                ["instanceId"] = instanceId ?? "",
                ["layout"] = layoutJson
            });
    }

    private static OverlayRealtimeEvent App(
        string type,
        string summary,
        IReadOnlyDictionary<string, string> data) =>
        new(
            Source: "app",
            Type: type,
            At: DateTimeOffset.UtcNow,
            Summary: summary,
            Data: data);
}
