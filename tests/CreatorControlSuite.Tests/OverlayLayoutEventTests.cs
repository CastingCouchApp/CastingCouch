using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayLayoutEventTests
{
    [Fact]
    public void AppOverlayLayout_PutsLayoutJsonInData()
    {
        var layout = OverlayLayout.CreateDefault();
        layout.Items.Add(new OverlayLayoutItem
        {
            Id = "1",
            Kind = "widget",
            Type = "online",
            X = 0,
            Y = 0,
            W = 200,
            H = 80,
            Z = 1
        });

        OverlayRealtimeEvent evt = OverlayEventBridge.AppOverlayLayout("instA", layout);
        Assert.Equal("app", evt.Source);
        Assert.Equal("app.overlay.layout", evt.Type);
        Assert.Equal("instA", evt.Data["instanceId"]);
        Assert.False(string.IsNullOrWhiteSpace(evt.Data["layout"]));

        using JsonDocument doc = JsonDocument.Parse(evt.Data["layout"]);
        Assert.Equal("online", doc.RootElement.GetProperty("items")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task PublishLayout_BroadcastsToClients()
    {
        var hub = new OverlayRealtimeHub();
        string? received = null;
        hub.Register(Guid.NewGuid(), (json, _) =>
        {
            received = json;
            return Task.CompletedTask;
        });

        OverlayRealtimeEvent evt = OverlayEventBridge.AppOverlayLayout(
            "i1",
            OverlayLayout.CreateDefault());
        await hub.PublishEventAsync(evt);

        Assert.False(string.IsNullOrWhiteSpace(received));
        using JsonDocument doc = JsonDocument.Parse(received!);
        Assert.Equal("app.overlay.layout", doc.RootElement.GetProperty("type").GetString());
    }
}
