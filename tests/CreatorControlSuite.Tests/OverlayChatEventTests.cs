using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class OverlayChatEventTests
{
    [Fact]
    public void FromChatMessage_SerializesPartsIntoData()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.FromChatMessage(
            messageId: "m1",
            userName: "Alice",
            userLogin: "alice",
            color: "#FF0000",
            badges:
            [
                new OverlayChatBadgePart("moderator", "1", "https://cdn/mod", "Moderator"),
                new OverlayChatBadgePart("subscriber", "12", "https://cdn/sub", "Subscriber")
            ],
            summary: "Alice: hi Kappa",
            at: DateTimeOffset.Parse("2026-07-27T18:00:00Z"),
            parts:
            [
                new OverlayChatMessagePart("text", "hi "),
                new OverlayChatMessagePart("emote", "Kappa", "https://cdn/kappa", "twitch")
            ]);

        Assert.Equal("twitch", evt.Source);
        Assert.Equal("channel.chat.message", evt.Type);
        Assert.Equal("Alice: hi Kappa", evt.Summary);
        Assert.Equal("m1", evt.Data["messageId"]);
        Assert.Equal("Alice", evt.Data["userName"]);
        Assert.Equal("alice", evt.Data["userLogin"]);
        Assert.Equal("#FF0000", evt.Data["color"]);

        using JsonDocument badgesDoc = JsonDocument.Parse(evt.Data["badges"]);
        Assert.Equal(2, badgesDoc.RootElement.GetArrayLength());
        Assert.Equal("moderator", badgesDoc.RootElement[0].GetProperty("setId").GetString());
        Assert.Equal("https://cdn/mod", badgesDoc.RootElement[0].GetProperty("url").GetString());

        using JsonDocument doc = JsonDocument.Parse(evt.Data["parts"]);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("emote", doc.RootElement[1].GetProperty("type").GetString());
        Assert.Equal("Kappa", doc.RootElement[1].GetProperty("text").GetString());
        Assert.Equal("https://cdn/kappa", doc.RootElement[1].GetProperty("url").GetString());
        Assert.Equal("twitch", doc.RootElement[1].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task PublishEventAsync_BuffersChatEvenWithoutClients()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(10);

        OverlayRealtimeEvent evt = OverlayEventBridge.FromChatMessage(
            "m1",
            "Alice",
            "alice",
            "",
            [],
            "Alice: hi",
            DateTimeOffset.UtcNow,
            [new OverlayChatMessagePart("text", "hi")]);

        await hub.PublishEventAsync(evt);

        IReadOnlyList<OverlayRealtimeEvent> buffered = hub.GetBufferedChatEvents();
        Assert.Single(buffered);
        Assert.Equal("m1", buffered[0].Data["messageId"]);
    }

    [Fact]
    public async Task PublishEventAsync_TrimsChatBufferToMax()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(2);

        for (int i = 1; i <= 3; i++)
        {
            await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
                $"m{i}",
                "A",
                "a",
                "",
                [],
                $"A: {i}",
                DateTimeOffset.UtcNow,
                [new OverlayChatMessagePart("text", i.ToString())]));
        }

        IReadOnlyList<OverlayRealtimeEvent> buffered = hub.GetBufferedChatEvents();
        Assert.Equal(2, buffered.Count);
        Assert.Equal("m2", buffered[0].Data["messageId"]);
        Assert.Equal("m3", buffered[1].Data["messageId"]);
    }

    [Fact]
    public async Task PublishEventAsync_DoesNotBufferNonChatEvents()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(10);

        await hub.PublishEventAsync(OverlayEventBridge.AppStreamLive(true));

        Assert.Empty(hub.GetBufferedChatEvents());
    }
}
