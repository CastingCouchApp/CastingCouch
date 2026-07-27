using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class OverlayRealtimeHubTests
{
    [Fact]
    public async Task PublishEventAsync_SendsSerializedEnvelopeToAllClients()
    {
        var hub = new OverlayRealtimeHub();
        var received = new List<string>();

        hub.Register(Guid.NewGuid(), (json, _) =>
        {
            received.Add(json);
            return Task.CompletedTask;
        });
        hub.Register(Guid.NewGuid(), (json, _) =>
        {
            received.Add(json);
            return Task.CompletedTask;
        });

        var evt = new OverlayRealtimeEvent(
            Source: "twitch",
            Type: "channel.follow",
            At: DateTimeOffset.Parse("2026-07-27T18:00:00Z"),
            Summary: "Neuer Follower",
            Data: new Dictionary<string, string> { ["user"] = "alice" });

        await hub.PublishEventAsync(evt);

        Assert.Equal(2, received.Count);
        Assert.All(received, json =>
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.Equal("twitch", root.GetProperty("source").GetString());
            Assert.Equal("channel.follow", root.GetProperty("type").GetString());
            Assert.Equal("Neuer Follower", root.GetProperty("summary").GetString());
            Assert.Equal("alice", root.GetProperty("data").GetProperty("user").GetString());
        });
    }

    [Fact]
    public async Task PublishEventAsync_RemovesFailingClients()
    {
        var hub = new OverlayRealtimeHub();
        Guid badId = Guid.NewGuid();
        Guid goodId = Guid.NewGuid();
        string? goodPayload = null;

        hub.Register(badId, (_, _) => throw new InvalidOperationException("gone"));
        hub.Register(goodId, (json, _) =>
        {
            goodPayload = json;
            return Task.CompletedTask;
        });

        await hub.PublishEventAsync(new OverlayRealtimeEvent(
            "app",
            "app.ws.hello",
            DateTimeOffset.UtcNow,
            "hello",
            new Dictionary<string, string>()));

        Assert.Equal(1, hub.ConnectedClients);
        Assert.False(string.IsNullOrWhiteSpace(goodPayload));
    }

    [Fact]
    public async Task PublishEventAsync_IgnoresNullEvent()
    {
        var hub = new OverlayRealtimeHub();
        int calls = 0;
        hub.Register(Guid.NewGuid(), (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await hub.PublishEventAsync(null!);
        Assert.Equal(0, calls);
    }
}
