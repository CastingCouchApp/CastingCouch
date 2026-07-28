using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class ChatHistoryStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundtripsChatEvents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ccs-chat-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ChatHistoryStore(path);
            OverlayRealtimeEvent evt = OverlayEventBridge.FromChatMessage(
                "m1",
                "Alice",
                "alice",
                "#FF0000",
                [],
                "Alice: hi",
                DateTimeOffset.Parse("2026-07-28T12:00:00Z"),
                [new OverlayChatMessagePart("text", "hi")],
                "42");

            await store.SaveAsync([evt]);
            IReadOnlyList<OverlayRealtimeEvent> loaded = await store.LoadAsync();

            Assert.Single(loaded);
            Assert.Equal("m1", loaded[0].Data["messageId"]);
            Assert.Equal("42", loaded[0].Data["userId"]);
            Assert.Equal("alice", loaded[0].Data["userLogin"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ccs-chat-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{not-json");
            var store = new ChatHistoryStore(path);
            IReadOnlyList<OverlayRealtimeEvent> loaded = await store.LoadAsync();
            Assert.Empty(loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Hub_RemoveMessage_RemovesFromBuffer()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(20);
        await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
            "m1", "A", "a", "", [], "A: 1", DateTimeOffset.UtcNow,
            [new OverlayChatMessagePart("text", "1")], "1"));
        await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
            "m2", "B", "b", "", [], "B: 2", DateTimeOffset.UtcNow,
            [new OverlayChatMessagePart("text", "2")], "2"));

        Assert.True(hub.RemoveBufferedChatMessage("m1"));
        IReadOnlyList<OverlayRealtimeEvent> buffered = hub.GetBufferedChatEvents();
        Assert.Single(buffered);
        Assert.Equal("m2", buffered[0].Data["messageId"]);
    }

    [Fact]
    public async Task Hub_ClearAndRestore_WorksWithCapacity()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(2);
        List<OverlayRealtimeEvent> seed =
        [
            OverlayEventBridge.FromChatMessage(
                "m1", "A", "a", "", [], "A: 1", DateTimeOffset.UtcNow,
                [new OverlayChatMessagePart("text", "1")]),
            OverlayEventBridge.FromChatMessage(
                "m2", "B", "b", "", [], "B: 2", DateTimeOffset.UtcNow,
                [new OverlayChatMessagePart("text", "2")]),
            OverlayEventBridge.FromChatMessage(
                "m3", "C", "c", "", [], "C: 3", DateTimeOffset.UtcNow,
                [new OverlayChatMessagePart("text", "3")])
        ];

        hub.RestoreBufferedChatEvents(seed);
        Assert.Equal(2, hub.GetBufferedChatEvents().Count);
        Assert.Equal("m2", hub.GetBufferedChatEvents()[0].Data["messageId"]);

        hub.ClearBufferedChat();
        Assert.Empty(hub.GetBufferedChatEvents());
    }

    [Fact]
    public async Task Hub_RemoveUserMessages_FiltersLogin()
    {
        var hub = new OverlayRealtimeHub();
        hub.ConfigureChatBuffer(20);
        await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
            "m1", "Alice", "alice", "", [], "Alice: 1", DateTimeOffset.UtcNow,
            [new OverlayChatMessagePart("text", "1")], "10"));
        await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
            "m2", "Bob", "bob", "", [], "Bob: 2", DateTimeOffset.UtcNow,
            [new OverlayChatMessagePart("text", "2")], "20"));

        Assert.Equal(1, hub.RemoveBufferedChatMessagesByUser("alice", ""));
        Assert.Equal("m2", hub.GetBufferedChatEvents().Single().Data["messageId"]);
    }

    [Fact]
    public async Task Store_Save_TrimsViaHubCapacity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ccs-chat-{Guid.NewGuid():N}.json");
        try
        {
            var hub = new OverlayRealtimeHub();
            hub.ConfigureChatBuffer(2);
            for (int i = 1; i <= 4; i++)
            {
                await hub.PublishEventAsync(OverlayEventBridge.FromChatMessage(
                    $"m{i}", "A", "a", "", [], $"A: {i}", DateTimeOffset.UtcNow,
                    [new OverlayChatMessagePart("text", i.ToString())]));
            }

            var store = new ChatHistoryStore(path);
            await store.SaveAsync(hub.GetBufferedChatEvents());
            IReadOnlyList<OverlayRealtimeEvent> loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(["m3", "m4"], loaded.Select(e => e.Data["messageId"]).ToArray());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AppChatClear_HasExpectedType()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppChatClear();
        Assert.Equal("app", evt.Source);
        Assert.Equal("app.chat.clear", evt.Type);
    }
}
